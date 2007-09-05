//
// linq.cs: support for query expressions
//
// Authors: Marek Safar (marek.safar@gmail.com)
//
// Licensed under the terms of the GNU GPL
//
// (C) 2007 Novell, Inc
//

using System;
using System.Reflection;
using System.Collections;

namespace Mono.CSharp.Linq
{
	// NOTES:
	// Expression should be IExpression to save some memory and make a few things
	// easier to read
	//
	//

	public class QueryExpression : ARangeVariableQueryClause
	{
		public QueryExpression (Block block, Expression from, AQueryClause query)
			: base (block, from, from.Location)
		{
			this.next = query;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			Expression e = BuildQueryClause (ec, expr, null, null);
			e = e.Resolve (ec);
			return e;
		}

		protected override string MethodName {
			get { throw new NotSupportedException (); }
		}
	}

	public abstract class ALinqExpression : Expression
	{
		// Dictionary of method name -> MethodGroupExpr
		static Hashtable methods = new Hashtable ();
		static Type enumerable_class;

		protected abstract string MethodName { get; }

		protected MethodGroupExpr MethodGroup {
			get {
				//
				// Even if C# spec indicates that LINQ methods are context specific
				// in reality they are hardcoded
				//				
				MemberList ml = (MemberList)methods [MethodName];
				if (ml == null) {
					if (enumerable_class == null)
						enumerable_class = TypeManager.CoreLookupType ("System.Linq", "Enumerable");

					ml = TypeManager.FindMembers (enumerable_class,
						MemberTypes.Method, BindingFlags.Static | BindingFlags.Public,
						Type.FilterName, MethodName);
				}
				
				return new MethodGroupExpr (ArrayList.Adapter (ml), enumerable_class, loc);
			}
		}
	}

	public abstract class AQueryClause : ALinqExpression
	{
		public AQueryClause next;
		/*protected*/ public Expression expr;

		protected AQueryClause (Expression expr, Location loc)
		{
			this.expr = expr;
			this.loc = loc;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			return expr.DoResolve (ec);
		}

		public override void Emit (EmitContext ec)
		{
			throw new NotSupportedException ();
		}

		public virtual Expression BuildQueryClause (EmitContext ec, Expression from, Parameter parameter, TransparentIdentifiersScope ti)
		{
			ArrayList args = new ArrayList (2);
			args.Add (new Argument (from));
			args.Add (new Argument (CreateSelector (ec, expr, parameter, ti)));
			expr = new Invocation (MethodGroup, args);
			if (next != null)
				return next.BuildQueryClause (ec, this, parameter, ti);

			return expr;
		}

		protected LambdaExpression CreateSelector (EmitContext ec, Expression expr, Parameter parameter, TransparentIdentifiersScope ti)
		{
			return CreateSelector (ec, expr, new Parameter [] { parameter }, ti);
		}

		protected LambdaExpression CreateSelector (EmitContext ec, Expression expr, Parameter[] parameters, TransparentIdentifiersScope ti)
		{
			Parameters p = new Parameters (parameters);

			LambdaExpression selector = new LambdaExpression (
				null, null, (TypeContainer) ec.DeclContainer, p, ec.CurrentBlock, loc);
			selector.Block = new SelectorBlock (ec.CurrentBlock, p, ti, loc);
			selector.Block.AddStatement (new Return (expr, loc));

			selector.CreateAnonymousHelpers ();
			selector.RootScope.DefineType ();

			return selector;
		}

		public virtual AQueryClause Next {
			set {
				next = value;
			}
		}

		public AQueryClause Tail {
			get {
				return next == null ? this : next.Tail;
			}
		}
	}

	//
	// A query clause with an identifier (range variable)
	//
	public abstract class ARangeVariableQueryClause : AQueryClause
	{
		public readonly Block block;
		protected Expression element_selector;

		protected ARangeVariableQueryClause (Block block, Expression expr, Location loc)
			: base (expr, loc)
		{
			this.block = block;
		}

		protected virtual void AddSelectorArguments (EmitContext ec, ArrayList args, Parameter parentParameter,
			Parameter parameter, TransparentIdentifiersScope ti)
		{
			args.Add (new Argument (CreateSelector (ec, expr, parentParameter, null)));
			args.Add (new Argument (CreateSelector (ec, element_selector,
				new Parameter [] { parentParameter, parameter }, ti)));
		}

		//
		// Customization for range variables which not only creates a lambda expression but
		// also builds a chain of range varible pairs
		//
		public override Expression BuildQueryClause (EmitContext ec, Expression from, Parameter parentParameter, TransparentIdentifiersScope ti)
		{
			ICollection values = block.Variables.Values;
			if (values.Count != 1)
				throw new NotImplementedException ("Count != 1");

			IEnumerator enumerator = values.GetEnumerator ();
			enumerator.MoveNext ();
			LocalInfo li = (LocalInfo) enumerator.Current;

			Parameter parameter;
			if (li.Type == ImplicitQueryParameter.ImplicitType.Instance) {
				parameter = new ImplicitQueryParameter (li);
			} else {
				parameter = new Parameter (li.Type, li.Name, Parameter.Modifier.NONE, null, li.Location);
			}

			if (parentParameter == null)
				return next.BuildQueryClause (ec, expr, parameter, ti);

			if (next != null) {
				//
				// Builds transparent identifiers, each identifier includes its parent
				// type at index 0, and new value at index 1. This is not valid for the
				// first one which includes two values directly.
				//
				ArrayList transp_args = new ArrayList (2);
				transp_args.Add (new AnonymousTypeParameter (parentParameter));
				transp_args.Add (CreateAnonymousTypeVariable (parameter));
				element_selector = new AnonymousTypeDeclaration (transp_args, (TypeContainer) ec.DeclContainer, loc);
			}

			ArrayList args = new ArrayList (3);
			args.Add (new Argument (from));
			AddSelectorArguments (ec, args, parentParameter, parameter, ti);

			expr = new Invocation (MethodGroup, args);
			if (next != null) {
				//
				// Parameter indentifiers goes to the scope
				//
				string[] identifiers;
				if (ti == null) {
					identifiers = new string [] { parentParameter.Name, parameter.Name };
				} else {
					identifiers = new string [] { parameter.Name };
				}

				TransparentParameter tp = new TransparentParameter (loc);
				return next.BuildQueryClause (ec, this, tp,
					new TransparentIdentifiersScope (ti, tp, identifiers));
			}

			return expr;
		}

		//
		// For transparent identifiers, creates an instance of variable expression
		//
		protected virtual AnonymousTypeParameter CreateAnonymousTypeVariable (Parameter parameter)
		{
			return new AnonymousTypeParameter (parameter);
		}
	}

	public class Cast : ALinqExpression
	{
		Expression expr;
		readonly Expression cast_type;

		public Cast (Expression type, Expression expr, Location loc)
		{
			this.cast_type = type;
			this.expr = expr;
			this.loc = loc;
		}

		protected override void CloneTo (CloneContext clonectx, Expression target)
		{
			// We don't have to clone cast type
			Cast t = (Cast) target;
			t.expr = expr.Clone (clonectx);
		}

		protected override string MethodName {
			get { return "Cast"; }
		}

		public override Expression DoResolve (EmitContext ec)
		{
			TypeArguments type_arguments = new TypeArguments (loc, cast_type);
			Expression cast = MethodGroup.ResolveGeneric (ec, type_arguments);

			ArrayList args = new ArrayList (1);
			args.Add (new Argument (expr));
			return new Invocation (cast, args).DoResolve (ec);
		}

		public override void Emit (EmitContext ec)
		{
			throw new NotSupportedException ();
		}
	}

	public class GroupBy : AQueryClause
	{
		readonly Expression element_selector;
		
		public GroupBy (Expression elementSelector, Expression keySelector, Location loc)
			: base (keySelector, loc)
		{
			this.element_selector = elementSelector;
		}

		public override Expression BuildQueryClause (EmitContext ec, Expression from, Parameter parameter, TransparentIdentifiersScope ti)
		{
			ArrayList args = new ArrayList (3);
			args.Add (new Argument (from));
			args.Add (new Argument (CreateSelector (ec, expr, parameter, ti)));

			// A query can be optimized when selector is not group by specific
			if (!element_selector.Equals (from))
				args.Add (new Argument (CreateSelector (ec, element_selector, parameter, ti)));

			expr = new Invocation (MethodGroup, args);
			if (next != null)
				return next.BuildQueryClause (ec, this, parameter, ti);

			return expr;
		}

		protected override string MethodName {
			get { return "GroupBy"; }
		}
	}

	public class Let : ARangeVariableQueryClause
	{
		class RangeAnonymousTypeParameter : AnonymousTypeParameter
		{
			readonly Parameter parameter;

			public RangeAnonymousTypeParameter (Expression initializer, Parameter parameter)
				: base (initializer, parameter.Name, parameter.Location)
			{
				this.parameter = parameter;
			}

			public override Expression DoResolve (EmitContext ec)
			{
				Expression e = base.DoResolve (ec);
				if (e != null) {
					//
					// Spread resolved initializer type
					//
					parameter.ParameterType = type;
					parameter.Resolve (ec);
				}

				return e;
			}
		}

		public Let (Block block, Expression expr)
			: base (block, expr, expr.Location)
		{
		}

		protected override void AddSelectorArguments (EmitContext ec, ArrayList args, Parameter parentParameter, Parameter parameter,
			TransparentIdentifiersScope ti)
		{
			args.Add (new Argument (CreateSelector (ec, element_selector, parentParameter, ti)));
		}

		protected override AnonymousTypeParameter CreateAnonymousTypeVariable (Parameter parameter)
		{
			return new RangeAnonymousTypeParameter (expr, parameter);
		}

		protected override string MethodName {
			get { return "Select"; }
		}
	}

	public class Select : AQueryClause
	{
		public Select (Expression expr, Location loc)
			: base (expr, loc)
		{
		}

		protected override string MethodName {
			get { return "Select"; }
		}
	}

	public class SelectMany : ARangeVariableQueryClause
	{
		public SelectMany (Block block, Expression expr)
			: base (block, expr, expr.Location)
		{
		}

		protected override string MethodName {
			get { return "SelectMany"; }
		}

		public override AQueryClause Next {
			set {
				element_selector = value.expr;

				// Can be optimized as SelectMany element selector
				if (value is Select)
					return;

				next = value;
			}
		}
	}

	public class Where : AQueryClause
	{
		public Where (Expression expr, Location loc)
			: base (expr, loc)
		{
		}

		protected override string MethodName {
			get { return "Where"; }
		}
	}

	public class OrderByAscending : AQueryClause
	{
		public OrderByAscending (Expression expr)
			: base (expr, expr.Location)
		{
		}

		protected override string MethodName {
			get { return "OrderBy"; }
		}
	}

	public class OrderByDescending : AQueryClause
	{
		public OrderByDescending (Expression expr)
			: base (expr, expr.Location)
		{
		}

		protected override string MethodName {
			get { return "OrderByDescending"; }
		}
	}

	public class ThenByAscending : OrderByAscending
	{
		public ThenByAscending (Expression expr)
			: base (expr)
		{
		}

		protected override string MethodName {
			get { return "ThenBy"; }
		}
	}

	public class ThenByDescending : OrderByDescending
	{
		public ThenByDescending (Expression expr)
			: base (expr)
		{
		}

		protected override string MethodName {
			get { return "ThenByDescending"; }
		}
	}

	class ImplicitQueryParameter : ImplicitLambdaParameter
	{
		public sealed class ImplicitType : Expression
		{
			public static ImplicitType Instance = new ImplicitType ();

			private ImplicitType ()
			{
			}

			protected override void CloneTo (CloneContext clonectx, Expression target)
			{
				// Nothing to clone
			}

			public override Expression DoResolve (EmitContext ec)
			{
				throw new NotSupportedException ();
			}

			public override void Emit (EmitContext ec)
			{
				throw new NotSupportedException ();
			}
		}

		readonly LocalInfo variable;

		public ImplicitQueryParameter (LocalInfo variable)
			: base (variable.Name, variable.Location)
		{
			this.variable = variable;
		}

		public override bool Resolve (IResolveContext ec)
		{
			if (!base.Resolve (ec))
				return false;

			variable.VariableType = parameter_type;
			return true;
		}
	}

	//
	// Transparent parameters are used to package up the intermediate results
	// and pass them onto next clause
	//
	public class TransparentParameter : ImplicitLambdaParameter
	{
		static int counter;
		const string ParameterNamePrefix = "<>__TranspIdent";

		public TransparentParameter (Location loc)
			: base (ParameterNamePrefix + counter++, loc)
		{
		}
	}

	//
	// Transparent identifiers are stored in nested anonymous types, each type can contain
	// up to 2 identifiers or 1 identifier and parent type.
	//
	public class TransparentIdentifiersScope
	{
		readonly string [] identifiers;
		readonly TransparentIdentifiersScope parent;
		readonly TransparentParameter parameter;

		public TransparentIdentifiersScope (TransparentIdentifiersScope parent,
			TransparentParameter parameter, string [] identifiers)
		{
			this.parent = parent;
			this.parameter = parameter;
			this.identifiers = identifiers;
		}

		public MemberAccess GetIdentifier (string name)
		{
			TransparentIdentifiersScope ident = FindIdentifier (name);
			if (ident == null)
				return null;

			return new MemberAccess (CreateIndentifierNestingExpression (ident), name);
		}

		TransparentIdentifiersScope FindIdentifier (string name)
		{
			foreach (string s in identifiers) {
				if (s == name)
					return this;
			}

			if (parent == null)
				return null;

			return parent.FindIdentifier (name);
		}

		Expression CreateIndentifierNestingExpression (TransparentIdentifiersScope end)
		{
			Expression expr = new SimpleName (parameter.Name, parameter.Location);
			TransparentIdentifiersScope current = this;
			while (current != end)
			{
				current = current.parent;
				expr = new MemberAccess (expr, current.parameter.Name);
			}

			return expr;
		}
	}

	//
	// Lambda expression block which contains transparent identifiers
	//
	class SelectorBlock : ToplevelBlock
	{
		readonly TransparentIdentifiersScope transparent_identifiers;

		public SelectorBlock (Block block, Parameters parameters, 
			TransparentIdentifiersScope transparentIdentifiers, Location loc)
			: base (block, parameters, loc)
		{
			this.transparent_identifiers = transparentIdentifiers;
		}

		public override Expression GetTransparentIdentifier (string name)
		{
			if (transparent_identifiers == null)
				return null;

			return transparent_identifiers.GetIdentifier (name);
		}
	}
}
