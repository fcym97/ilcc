﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ilcclib.New.Ast;
using System.Xml.Linq;
using ilcclib.New.Types;

namespace ilcclib.New.Parser
{
	public partial class CParser
	{
		public Expression ParseExpressionUnary(Context Context)
		{
			Expression Result = null;

			while (true)
			{
			NextToken: ;
				var Current = Context.TokenCurrent;
				switch (Current.Type)
				{
					case CTokenType.Number:
						{
							Result = Context.TokenMoveNext(new IntegerExpression(int.Parse(Current.Raw)));
							goto PostOperations;
						}
					case CTokenType.Identifier:
						{
							switch (Current.Raw)
							{
								case "__extension__":
									Context.TokenMoveNext();
									goto NextToken;
								case "__func__":
									Result = Context.TokenMoveNext(new SpecialIdentifierExpression(Current.Raw));
									goto PostOperations;
								default:
									Result = Context.TokenMoveNext(new IdentifierExpression(Current.Raw));
									goto PostOperations;
							}
						}
					case CTokenType.Operator:
						{
							switch (Current.Raw)
							{
								case "(":
									{
										Context.TokenMoveNext();
										Result = ParseExpression(Context);
										Context.TokenRequireAnyAndMove(")");
										goto PostOperations;
									}
								case "&":
								case "*":
								case "!":
								case "~":
								case "+":
								case "-":
									Context.TokenMoveNext();
									return new UnaryExpression(Current.Raw, ParseExpressionUnary(Context), OperatorPosition.Left);
								case "--":
								case "++":
									Context.TokenMoveNext();
									return new UnaryExpression(Current.Raw, ParseExpressionUnary(Context), OperatorPosition.Left);
								case "sizeof":
								case "__alignof":
								case "__alignof__":
									throw(new NotImplementedException());
								default:
									throw(new NotImplementedException());
							}
						}
					default:
						throw(new NotImplementedException());
				}
			}

			PostOperations: ;

			while (true)
			{
				var Current = Context.TokenCurrent;

				switch (Current.Raw)
				{
					// Post operations
					case "++":
					case "--":
						Context.TokenMoveNext();
						Result = new UnaryExpression(Current.Raw, Result, OperatorPosition.Right);
						break;
					// Field access
					case ".":
					case "->":
						throw(new NotImplementedException());
					// Array access
					case "[":
						{
							Context.TokenMoveNext();
							var Index = ParseExpression(Context);
							Context.TokenRequireAnyAndMove("]");
							return new ArrayAccessExpression(Result, Index);
						}
					// Function call
					case "(":
						throw (new NotImplementedException());
					default:
						goto End;
				}
			}

			End:;

			return Result;
		}
		public Expression ParseExpressionProduct(Context Context) { return _ParseExpressionStep(ParseExpressionUnary, COperators.OperatorsProduct, Context); }
		public Expression ParseExpressionSum(Context Context) { return _ParseExpressionStep(ParseExpressionProduct, COperators.OperatorsSum, Context); }
		public Expression ParseExpressionShift(Context Context) { return _ParseExpressionStep(ParseExpressionSum, COperators.OperatorsShift, Context); }
		public Expression ParseExpressionInequality(Context Context) { return _ParseExpressionStep(ParseExpressionShift, COperators.OperatorsInequality, Context); }
		public Expression ParseExpressionEquality(Context Context) { return _ParseExpressionStep(ParseExpressionInequality, COperators.OperatorsEquality, Context); }
		public Expression ParseExpressionAnd(Context Context) { return _ParseExpressionStep(ParseExpressionEquality, COperators.OperatorsAnd, Context); }
		public Expression ParseExpressionXor(Context Context) { return _ParseExpressionStep(ParseExpressionAnd, COperators.OperatorsXor, Context); }
		public Expression ParseExpressionOr(Context Context) { return _ParseExpressionStep(ParseExpressionXor, COperators.OperatorsOr, Context); }
		public Expression ParseExpressionLogicalAnd(Context Context) { return _ParseExpressionStep(ParseExpressionOr, COperators.OperatorsLogicalAnd, Context); }
		public Expression ParseExpressionLogicalOr(Context Context) { return _ParseExpressionStep(ParseExpressionLogicalAnd, COperators.OperatorsLogicalOr, Context); }

		public Expression ParseExpressionTernary(Context Context)
		{
			// TODO:
			var Left = ParseExpressionLogicalOr(Context);
			var Current = Context.TokenCurrent.Raw;
			if (Current == "?")
			{
				Context.TokenMoveNext();
				var TrueCond = ParseExpression(Context);
				Context.TokenRequireAnyAndMove(":");
				var FalseCond = ParseExpressionTernary(Context);
				Left = new TrinaryExpression(Left, TrueCond, FalseCond);
			}
			return Left;
		}

		private Expression _ParseExpressionStep(Func<Context, Expression> ParseLeftRightExpression, HashSet<string> Operators, Context Context)
		{
			return _ParseExpressionStep(ParseLeftRightExpression, ParseLeftRightExpression, Operators, Context);
		}

		private Expression _ParseExpressionStep(Func<Context, Expression> ParseLeftExpression, Func<Context, Expression> ParseRightExpression, HashSet<string> Operators, Context Context)
		{
			Expression Left;
			Expression Right;

			Left = ParseLeftExpression(Context);

			while (true)
			{
				var Operator = Context.TokenCurrent.Raw;
				if (!Operators.Contains(Operator))
				{
					//Console.WriteLine("Not '{0}' in '{1}'", Operator, String.Join(",", Operators));
					break;
				}
				Context.TokenMoveNext();
				Right = ParseRightExpression(Context);
				Left = new BinaryExpression(Left, Operator, Right);
			}

			return Left;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="Context"></param>
		/// <returns></returns>
		public Expression ParseExpressionAssign(Context Context)
		{
			//return _ParseExpressionStep();

			Expression Left;
			
			Left = ParseExpressionTernary(Context);

			var Operator = Context.TokenCurrent.Raw;
			if (COperators.OperatorsAssign.Contains(Operator))
			{
				Left.CheckLeftValue();
				Context.TokenMoveNext();
				var Right = ParseExpressionAssign(Context);
				Left = new BinaryExpression(Left, Operator, Right);
			}

			return Left;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="Context"></param>
		/// <returns></returns>
		public Expression ParseExpression(Context Context)
		{
			var Nodes = new List<Expression>();

			while (true)
			{
				Nodes.Add(ParseExpressionAssign(Context));
				if (Context.TokenIsCurrentAny(","))
				{
					// EmitPop
					Context.TokenMoveNext();
				}
				else
				{
					break;
				}
			}

			return new ExpressionCommaList(Nodes.ToArray());
		}

		public CompoundStatement ParseCompoundBlock(Context Context)
		{
			var Nodes = new List<Statement>();
			Context.TokenRequireAnyAndMove("{");
			while (!Context.TokenIsCurrentAny("}"))
			{
				Nodes.Add(ParseBlock(Context));
			}
			Context.TokenMoveNext();
			return new CompoundStatement(Nodes.ToArray());
		}

		public Statement ParseIfStatement(Context Context)
		{
			Expression Condition;
			Statement TrueStatement;
			Statement FalseStatement;

			Context.TokenRequireAnyAndMove("if");
			Context.TokenRequireAnyAndMove("(");
			Condition = ParseExpression(Context);
			Context.TokenRequireAnyAndMove(")");
			TrueStatement = ParseBlock(Context);

			if (Context.TokenIsCurrentAny("else"))
			{
				Context.TokenRequireAnyAndMove("else");
				FalseStatement = ParseBlock(Context);
			}
			else
			{
				FalseStatement = null;
			}

			return new IfElseStatement(Condition, TrueStatement, FalseStatement);
		}

		public CType TryParseBasicType(Context Context)
		{
			var BasicTypes = new List<CType>();

			while (true)
			{
				var Current = Context.TokenCurrent;
				switch (Current.Type)
				{
					case CTokenType.Identifier:
						switch (Current.Raw)
						{
							// Ignore those.
							case "__extension__":
							case "register":
							case "auto":
							case "restrict":
							case "__restrict":
							case "__restrict__":
								Context.TokenMoveNext();
								break;
							case "char": BasicTypes.Add(new CBasicType(CBasicTypeType.Char)); Context.TokenMoveNext(); break;
							case "void": BasicTypes.Add(new CBasicType(CBasicTypeType.Void)); Context.TokenMoveNext(); break;
							case "short": BasicTypes.Add(new CBasicType(CBasicTypeType.Short)); Context.TokenMoveNext(); break;
							case "int": BasicTypes.Add(new CBasicType(CBasicTypeType.Int)); Context.TokenMoveNext(); break;
							case "long": BasicTypes.Add(new CBasicType(CBasicTypeType.Long)); Context.TokenMoveNext(); break;
							case "_Bool": BasicTypes.Add(new CBasicType(CBasicTypeType.Bool)); Context.TokenMoveNext(); break;
							case "float": BasicTypes.Add(new CBasicType(CBasicTypeType.Float)); Context.TokenMoveNext(); break;
							case "double": BasicTypes.Add(new CBasicType(CBasicTypeType.Double)); Context.TokenMoveNext(); break;
							case "enum":
							case "struct":
							case "union":
								throw (new NotImplementedException());
							case "const":
							case "__const":
							case "__const__":
								BasicTypes.Add(new CBasicType(CBasicTypeType.Const)); Context.TokenMoveNext(); break;
							case "volatile":
							case "__volatile":
							case "__volatile__":
								BasicTypes.Add(new CBasicType(CBasicTypeType.Volatile)); Context.TokenMoveNext(); break;
							case "signed":
							case "__signed":
							case "__signed__":
								BasicTypes.Add(new CBasicType(CBasicTypeType.Signed)); Context.TokenMoveNext(); break;
							case "unsigned":
								BasicTypes.Add(new CBasicType(CBasicTypeType.Unsigned)); Context.TokenMoveNext(); break;
							case "extern":
								BasicTypes.Add(new CBasicType(CBasicTypeType.Extern)); Context.TokenMoveNext(); break;
							case "static":
								BasicTypes.Add(new CBasicType(CBasicTypeType.Static)); Context.TokenMoveNext(); break;
							case "typedef":
								BasicTypes.Add(new CBasicType(CBasicTypeType.Typedef)); Context.TokenMoveNext(); break;
							case "inline":
							case "__inline":
							case "__inline__":
								BasicTypes.Add(new CBasicType(CBasicTypeType.Inline)); Context.TokenMoveNext(); break;
							case "__attribute":
							case "__attribute__":
								throw (new NotImplementedException());
							case "typeof":
							case "__typeof":
							case "__typeof__":
								throw (new NotImplementedException());
							default:
								{
									var Symbol = Context.SymbolFind(Current.Raw);
									if (Symbol != null && Symbol.IsType)
									{
										Context.TokenMoveNext(); break;
									}
									else
									{
										goto End;
									}
								}
						}
						break;
					default:
						goto End;
				}
			}

		End: ;

			if (BasicTypes.Count != 0)
			{
				return new CCompoundType(BasicTypes.ToArray());
			}
			else
			{
				return null;
			}
		}

		/// <summary>
		/// Parses an statement
		/// </summary>
		/// <param name="Context"></param>
		/// <returns></returns>
		public Statement ParseBlock(Context Context)
		{
			var Current = Context.TokenCurrent;

			switch (Current.Raw)
			{
				case "if": return ParseIfStatement(Context);
				case "switch":
					throw (new NotImplementedException());
				case "case":
					throw (new NotImplementedException());
				case "default":
					throw (new NotImplementedException());
				case "goto":
					throw (new NotImplementedException());
				case "asm":
				case "__asm":
				case "__asm__":
					throw (new NotImplementedException());
				case "while":
					throw (new NotImplementedException());
				case "for":
					throw (new NotImplementedException());
				case "do":
					throw (new NotImplementedException());
				case "break":
					throw (new NotImplementedException());
				case "continue":
					throw (new NotImplementedException());
				case "return":
					throw (new NotImplementedException());
				case "{":
					return ParseCompoundBlock(Context);
				case ";":
					Context.TokenMoveNext();
					return new CompoundStatement(new Statement[] {});
				default:
					{
						var BasicType = TryParseBasicType(Context);
						if (BasicType != null)
						{
							// Type Declaration
							//ParseBlock();
							throw (new NotImplementedException());
						}
						// Expression
						else
						{
							ParseExpression(Context);
							throw(new NotImplementedException());
						}
					}
					// LABEL
					// EXPRESSION + ;
					throw (new NotImplementedException());
			}
		}

		static public TType StaticParse<TType>(string Text, Func<CParser, Context, TType> ParserAction) where TType : Node
		{
			var Tokenizer = new CTokenizer();
			var Parser = new CParser();
			var Context = new CParser.Context(Tokenizer.Tokenize(Text).GetEnumerator());
			var Result = ParserAction(Parser, Context);
			Context.CheckReadedAllTokens();
			return Result;
		}

		static public Expression StaticParseExpression(string Text)
		{
			return StaticParse(Text, (Parser, Context) => { return Parser.ParseExpression(Context); });
		}

		static public Statement StaticParseBlock(string Text)
		{
			return StaticParse(Text, (Parser, Context) => { return Parser.ParseBlock(Context); });
		}

		public Node ParseProgram(Context Context)
		{
			Context.CreateScope(() =>
			{
			});
			//Context.Tokens.
			throw(new NotImplementedException());
		}
	}
}
