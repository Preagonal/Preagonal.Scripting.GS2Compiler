using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Preagonal.Scripting.Common;
using Preagonal.Scripting.GSAst;

namespace Preagonal.Scripting.GS2Compiler;

internal static class GS2Compiler
{
	public static CompilerResponse Compile(string code, string type, string name, bool withHeader, ScriptGrammar grammar)
	{
		var parseResult = grammar switch
		{
			ScriptGrammar.GS2 => AntlrGs2Parser.Parse(code),
			ScriptGrammar.GS1 => AntlrGs1Parser.Parse(code),
			_                 => AntlrGs2Parser.Parse(code),
		};
		if (parseResult.ErrorMessage != null || parseResult.Program == null) return new() { Success = false, ErrMsg = parseResult.ErrorMessage ?? "malformed input\n", ByteCode = [] };
		var program  = parseResult.Program;
		var bytecode = new BytecodeWriter(program.Gs1EventFlags, grammar);
		var emitter  = new Emitter(bytecode, program.Constants, grammar);
		foreach (var item in program.Items)
		{
			if (item is FunctionItem function) emitter.EmitFunction(function.Function);
			else if (item is StatementItem statement)
			{
				bytecode.PatchPrejumps();
				emitter.EmitStatement(statement.Statement);
			}
		}

		var result = bytecode.ToArray();
		return new() { Success = true, ErrMsg = null, ByteCode = withHeader ? Header.Wrap(result, type, name) : result };
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private static T[] OrderByNativeHash<T>(IEnumerable<T> values, Func<T, string> name)
	{
		// Native THashList starts at 32 buckets, grows eightfold, and prepends collisions.
		var buckets = Enumerable.Range(0, 32).Select(_ => new List<T>()).ToArray();
		var count   = 0;
		foreach (var value in values)
		{
			if (count == buckets.Length)
			{
				var previous = buckets;
				buckets = Enumerable.Range(0, previous.Length * 8).Select(_ => new List<T>()).ToArray();
				foreach (var entry in previous.SelectMany(bucket => bucket))
					buckets[GraalNameHash(name(entry)) & (uint)(buckets.Length - 1)].Insert(0, entry);
			}

			buckets[GraalNameHash(name(value)) & (uint)(buckets.Length - 1)].Insert(0, value);
			count++;
		}

		return buckets.SelectMany(bucket => bucket).ToArray();
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private static uint GraalNameHash(string name)
	{
		var hash = 0x1505u;
		foreach (var value in Encoding.UTF8.GetBytes(name))
		{
			var character = value is >= (byte)'A' and <= (byte)'Z' ? value + 32 : (int)(sbyte)value;
			hash = unchecked(hash * 17 ^ (uint)character);
		}

		return hash;
	}

	private sealed record ParseResult(ProgramNode? Program, string? ErrorMessage);

	private sealed class ParseFailure(int line) : Exception
	{
		public int Line { get; } = line;
	}

	private sealed class ThrowingErrorListener : IAntlrErrorListener<int>, IAntlrErrorListener<IToken>
	{
		public void SyntaxError(
			TextWriter output,
			IRecognizer recognizer,
			int offendingSymbol,
			int line,
			int charPositionInLine,
			string msg,
			RecognitionException e
		) =>
			throw new ParseFailure(line);

		public void SyntaxError(
			TextWriter output,
			IRecognizer recognizer,
			IToken offendingSymbol,
			int line,
			int charPositionInLine,
			string msg,
			RecognitionException e
		) =>
			throw new ParseFailure(line);
	}

	private static string ErrorMessage(string code, int line) => $"malformed input at line {line}: {LineText(code, line)}\n";

	private static string LineText(string code, int line)
	{
		var currentLine = 1;
		var start       = 0;
		while (currentLine < line && start < code.Length)
		{
			var next = code.IndexOf('\n', start);
			if (next < 0) return "";
			start = next + 1;
			currentLine++;
		}

		var end          = code.IndexOf('\n', start);
		if (end < 0) end = code.Length;
		if (end > start && code[end - 1] == '\r') end--;
		return code[start..end];
	}

	private static string NormalizeOperator(string op) =>
		op switch
		{
			":=" => "=",
			"<>" => "!=",
			_    => op,
		};

	private static class AntlrGs2Parser
	{
		public static ParseResult Parse(string code)
		{
			try
			{
				var input    = CharStreams.fromString(code);
				var lexer    = new GS2Lexer(input);
				var tokens   = new CommonTokenStream(lexer);
				var parser   = new GS2Parser(tokens);
				var listener = new ThrowingErrorListener();
				lexer.RemoveErrorListeners();
				parser.RemoveErrorListeners();
				lexer.AddErrorListener(listener);
				parser.AddErrorListener(listener);
				var script = parser.script();
				return parser.NumberOfSyntaxErrors == 0 ? new(new Builder().Build(script), null) : new(null, ErrorMessage(code, 1));
			}
			catch (ParseFailure ex)
			{
				return new(null, ErrorMessage(code, ex.Line));
			}
		}

		private sealed class Builder
		{
			private readonly Dictionary<string, Expr>                    _constants = new(StringComparer.Ordinal);
			private readonly Dictionary<string, Dictionary<string, int>> _enums     = new(StringComparer.Ordinal);
			private          bool                                        _comparisonContext;
			private          int                                         _lambdaId = 1;

			public ProgramNode Build(GS2Parser.ScriptContext context)
			{
				List<ProgramItem> items         = [];
				var               gs1EventFlags = 0;
				foreach (var declaration in context.declaration())
				{
					if (declaration.constDeclaration() is { } constDeclaration) BuildConst(constDeclaration);
					else if (declaration.enumDeclaration() is { } enumDeclaration) BuildEnum(enumDeclaration);
					else if (declaration.functionDeclaration() is { } functionDeclaration) items.Add(new FunctionItem(BuildFunction(functionDeclaration)));
					else if (declaration.statement() is { } statement)
					{
						var builtStatement = BuildStatement(statement);
						gs1EventFlags |= Gs1EventFlagScanner.Scan(builtStatement);
						items.Add(new StatementItem(builtStatement));
					}
				}

				return new(_constants, _enums, items, gs1EventFlags);
			}

			private void BuildConst(GS2Parser.ConstDeclarationContext context) => _constants[context.IDENTIFIER().GetText()] = BuildExpression(context.expression());

			private void BuildEnum(GS2Parser.EnumDeclarationContext context)
			{
				var                     enumName = context.IDENTIFIER().GetText();
				Dictionary<string, int> values   = new(StringComparer.Ordinal);
				var                     index    = 0;
				foreach (var member in context.enumMember())
				{
					if (member.expression() != null) index = ParseEnumValue(member.expression());
					values[member.IDENTIFIER().GetText()] = index++;
				}

				_enums[enumName] = values;
			}

			private int ParseEnumValue(GS2Parser.ExpressionContext context) =>
				BuildExpression(context) switch
				{
					NumberExpr number                                    => int.Parse(number.Text, CultureInfo.InvariantCulture),
					UnaryExpr { Op: "-", Expression: NumberExpr number } => -int.Parse(number.Text, CultureInfo.InvariantCulture),
					_                                                    => 0,
				};

			private FunctionNode BuildFunction(GS2Parser.FunctionDeclarationContext context)
			{
				var (name, objectName) = BuildFunctionName(context.functionName().GetText());
				return new(name, objectName, context.PUBLIC() != null, BuildArguments(context.parameterList()?.argumentList()), BuildFunctionBody(context.statement()));
			}

			private static (string Name, string? ObjectName) BuildFunctionName(string text)
			{
				var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
				return parts.Length <= 1 ? (text, null) : (parts[^1], string.Join('.', parts.Take(parts.Length - 1)));
			}

			private List<Stmt> BuildStatementBody(GS2Parser.StatementContext? context)
			{
				if (context == null) return [];
				if (context.block() is { } block) return BuildBlock(block);
				if (IsEmptyStatement(context)) return [];
				return [new InlineStmt(BuildStatement(context))];
			}

			private List<Stmt> BuildFunctionBody(GS2Parser.StatementContext? context)
			{
				if (context == null) return [];
				if (context.block() is { } block) return BuildBlock(block);
				if (IsEmptyStatement(context)) return [];
				return [BuildStatement(context)];
			}

			private List<Stmt> BuildBlock(GS2Parser.BlockContext context)
			{
				List<Stmt> statements = [];
				foreach (var statement in context.statement())
				{
					var built = BuildStatement(statement);
					if (built is BlockStmt { Body.Count: 0 }) continue;
					statements.Add(built);
				}

				return statements;
			}

			private Stmt BuildStatement(GS2Parser.StatementContext context)
			{
				if (IsEmptyStatement(context)) return new BlockStmt([]);
				if (context.block() is { } block) return new BlockStmt(BuildBlock(block));
				if (context.ifStatement() is { } ifStatement) return BuildIf(ifStatement);
				if (context.forStatement() is { } forStatement) return BuildFor(forStatement);
				if (context.whileStatement() is { } whileStatement) return new WhileStmt(BuildCondition(whileStatement.expression()), BuildStatementBody(whileStatement.statement()));
				if (context.switchStatement() is { } switchStatement) return BuildSwitch(switchStatement);
				if (context.withStatement() is { } withStatement) return new WithStmt(BuildExpression(withStatement.expression()), BuildStatementBody(withStatement.statement()));
				if (context.newStatement() is { } newStatement) return new NewStmt(newStatement.IDENTIFIER().GetText(), BuildArguments(newStatement.argumentList()), newStatement.block() == null ? [] : BuildBlock(newStatement.block()));
				if (context.returnStatement() is { } returnStatement) return new ReturnStmt(returnStatement.expression() == null ? new NumberExpr("0") : BuildExpression(returnStatement.expression()));
				if (context.breakStatement() is { } breakStatement) return new BreakStmt { Label          = breakStatement.IDENTIFIER()?.GetText() };
				if (context.continueStatement() is { } continueStatement) return new ContinueStmt { Label = continueStatement.IDENTIFIER()?.GetText() };
				if (context.gotoStatement() is { } gotoStatement) return new GotoStmt(gotoStatement.IDENTIFIER().GetText());
				if (context.labelStatement() is { } labelStatement)
				{
					var label = labelStatement.IDENTIFIER().GetText();
					if (labelStatement.statement() is not { } target) return new LabelStmt(label);
					var statement = BuildStatement(target);
					statement = statement switch
					{
						ForStmt loop     => loop with { Label = label },
						ForEachStmt loop => loop with { Label = label },
						WhileStmt loop   => loop with { Label = label },
						_                => statement,
					};
					return new BlockStmt([new LabelStmt(label), statement]);
				}

				if (context.expressionStatement() is { } expressionStatement)
				{
					var expression = BuildExpression(expressionStatement.expression());
					return expression switch
					{
						IdentifierExpr { Name: "destroy" or "hide" or "show" or "enableweapons" } command => new ExprStmt(new CallExpr(command.Name, [])),
						IdentifierExpr or MemberExpr { Object: IdentifierExpr }                           => new BlockStmt([]),
						_                                                                                 => new ExprStmt(expression),
					};
				}

				throw Failure(context);
			}

			private static bool IsEmptyStatement(GS2Parser.StatementContext context) => context.SEMI() != null && context.ChildCount == 1;

			private IfStmt BuildIf(GS2Parser.IfStatementContext context)
			{
				var        hasElse  = context.ELSE() != null || context.ELSEIF() != null;
				List<Stmt> elseBody = [];
				if (context.ELSE() != null) elseBody        = BuildStatementBody(context.statement(1));
				else if (context.ifTail() != null) elseBody = [BuildIfTail(context.ifTail())];
				return new(BuildCondition(context.expression()), BuildStatementBody(context.statement(0)), elseBody, hasElse);
			}

			private IfStmt BuildIfTail(GS2Parser.IfTailContext context)
			{
				var        hasElse  = context.ELSE() != null || context.ELSEIF() != null;
				List<Stmt> elseBody = [];
				if (context.ELSE() != null) elseBody        = BuildStatementBody(context.statement(1));
				else if (context.ifTail() != null) elseBody = [BuildIfTail(context.ifTail())];
				return new(BuildCondition(context.expression()), BuildStatementBody(context.statement(0)), elseBody, hasElse);
			}

			private Stmt BuildFor(GS2Parser.ForStatementContext context)
			{
				var expressions = context.expression();
				if (context.COLON() != null) return new ForEachStmt(BuildExpression(expressions[0]), BuildExpression(expressions[1]), BuildStatementBody(context.statement()));
				Expr? init = null;
				Expr? post = null;
				var   expressionIndex = 0;
				if (expressions.Length > expressionIndex && expressions[expressionIndex].Start.TokenIndex < context.SEMI(0).Symbol.TokenIndex) init = BuildExpression(expressions[expressionIndex++]);
				var condition = expressions.Length > expressionIndex && expressions[expressionIndex].Start.TokenIndex < context.SEMI(1).Symbol.TokenIndex ? BuildExpression(expressions[expressionIndex++]) : new BoolExpr(true);
				if (expressions.Length > expressionIndex) post = BuildExpression(expressions[expressionIndex]);
				return new ForStmt(init, condition, post, BuildStatementBody(context.statement()));
			}

			private SwitchStmt BuildSwitch(GS2Parser.SwitchStatementContext context)
			{
				List<SwitchCase> cases  = [];
				List<Expr?>      labels = [];
				foreach (var switchCase in context.switchCase())
				{
					labels.Add(switchCase.DEFAULT() != null ? null : BuildExpression(switchCase.expression()));
					var statements = switchCase.statement().Select(BuildStatement).ToList();
					if (statements.Count == 0) continue;
					cases.Add(new([.. labels], statements));
					labels.Clear();
				}

				if (labels.Count > 0)
				{
					cases.Add(new([.. labels], []));
				}

				return new(BuildExpression(context.expression()), cases);
			}

			private Expr BuildExpression(GS2Parser.ExpressionContext context) => BuildAssignment(context.assignmentExpression());

			[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
			private Expr BuildCondition(GS2Parser.ExpressionContext context)
			{
				var comparisonContext = _comparisonContext;
				_comparisonContext = true;
				try
				{
					return BuildExpression(context);
				}
				finally
				{
					_comparisonContext = comparisonContext;
				}
			}

			private Expr BuildAssignment(GS2Parser.AssignmentExpressionContext context)
			{
				var left = BuildConditional(context.conditionalExpression());
				if (context.assignmentOperator() == null) return left;
				var comparisonContext = _comparisonContext;
				var op                = NormalizeOperator(context.assignmentOperator().GetText());
				_comparisonContext = false;
				try
				{
					return new BinaryExpr(left, comparisonContext && op == "=" ? "==" : op, BuildAssignment(context.assignmentExpression()));
				}
				finally
				{
					_comparisonContext = comparisonContext;
				}
			}

			private Expr BuildConditional(GS2Parser.ConditionalExpressionContext context)
			{
				var condition = BuildLogicalOr(context.logicalOrExpression());
				return context.QUESTION() == null ? condition : new TernaryExpr(condition, BuildExpression(context.expression()), BuildConditional(context.conditionalExpression()));
			}

			private Expr BuildLogicalOr(GS2Parser.LogicalOrExpressionContext context)   => BuildBinaryChain(context, context.logicalAndExpression(), BuildLogicalAnd);
			private Expr BuildLogicalAnd(GS2Parser.LogicalAndExpressionContext context) => BuildBinaryChain(context, context.bitwiseExpression(), BuildBitwise);
			private Expr BuildBitwise(GS2Parser.BitwiseExpressionContext context)       => BuildBinaryChain(context, context.equalityExpression(), BuildEquality);

			private Expr BuildEquality(GS2Parser.EqualityExpressionContext context)
			{
				var comparisons     = context.comparisonExpression();
				var ranges          = context.rangeExpression();
				var comparisonIndex = 1;
				var rangeIndex      = 0;
				var expression      = BuildComparison(comparisons[0]);
				for (var i = 1; i < context.ChildCount; ++i)
				{
					if (context.GetChild(i) is not ITerminalNode terminal) continue;
					if (terminal.Symbol.Type == GS2Parser.IN) expression = BuildInRange(expression, ranges[rangeIndex++]);
					else expression                                      = new BinaryExpr(expression, NormalizeOperator(terminal.GetText()), BuildComparison(comparisons[comparisonIndex++]));
				}

				return expression;
			}

			private Expr BuildInRange(Expr left, GS2Parser.RangeExpressionContext context)
			{
				if (context.comparisonExpression() != null) return new InExpr(left, BuildComparison(context.comparisonExpression()), null);
				var expressions = context.expression();
				return new InExpr(left, BuildExpression(expressions[0]), expressions.Length > 1 ? BuildExpression(expressions[1]) : null);
			}

			private Expr BuildComparison(GS2Parser.ComparisonExpressionContext context)         => BuildBinaryChain(context, context.concatExpression(), BuildConcat);
			private Expr BuildConcat(GS2Parser.ConcatExpressionContext context)                 => BuildBinaryChain(context, context.additiveExpression(), BuildAdditive);
			private Expr BuildAdditive(GS2Parser.AdditiveExpressionContext context)             => BuildBinaryChain(context, context.multiplicativeExpression(), BuildMultiplicative);
			private Expr BuildMultiplicative(GS2Parser.MultiplicativeExpressionContext context) => BuildBinaryChain(context, context.prefixExpression(), BuildPrefix);

			private Expr BuildPrefix(GS2Parser.PrefixExpressionContext context)
			{
				if (context.postfixExpression() != null) return BuildPostfix(context.postfixExpression());
				var op         = context.op.Text;
				var expression = BuildPrefix(context.prefixExpression());
				return op switch
				{
					"@"                                     => new StringCastExpr(expression),
					"!" when expression is BoolExpr boolean => new BoolExpr(!boolean.Value),
					_                                       => new UnaryExpr(op, expression),
				};
			}

			private Expr BuildPostfix(GS2Parser.PostfixExpressionContext context)
			{
				var expression = BuildPrimary(context.primaryExpression());
				foreach (var part in context.postfixPart())
				{
					if (part.DOT() != null && part.IDENTIFIER() != null)
					{
						var name = part.IDENTIFIER().GetText();
						expression = expression is IdentifierExpr identifier && _enums.TryGetValue(identifier.Name, out var members) && members.TryGetValue(name, out var value)
							? new NumberExpr(value.ToString(CultureInfo.InvariantCulture))
							: new MemberExpr(expression, name);
						continue;
					}

					if (part.DOT() != null && part.expression() != null)
					{
						expression = new DynamicMemberExpr(expression, BuildDynamicMemberName(part.expression()));
						continue;
					}

					if (part.LBRACK() != null)
					{
						expression = ApplyIndices(expression, BuildArguments(part.argumentList()), true);
						continue;
					}

					if (part.op != null)
					{
						expression = new UnaryExpr(part.op.Text, expression, true);
						continue;
					}

					if (expression is IdentifierExpr { Name: "imgwidth" or "imgheight" } legacyImageCall)
					{
						var interval = new Antlr4.Runtime.Misc.Interval(part.LPAREN().Symbol.StopIndex + 1, part.RPAREN().Symbol.StartIndex - 1);
						var argument = part.Start.InputStream.GetText(interval).TrimStart();
						expression = new CallExpr("get" + legacyImageCall.Name, [new StringExpr(argument)]);
						continue;
					}

					var args = BuildArguments(part.argumentList());
					if (expression is IdentifierExpr { Name: "sarraylen" } && part.argumentList() is { } stringArguments)
						args = stringArguments.expression().Select(argument => (Expr)new StringExpr(argument.GetText())).ToList();
					expression = expression switch
					{
						IdentifierExpr id        => new CallExpr(id.Name, args),
						MemberExpr member        => new MethodCallExpr(member.Object, member.Name, args),
						DynamicMemberExpr member => new DynamicMethodCallExpr(member.Object, member.Name, args),
						CallExpr call            => new ChainedCallExpr(call, args),
						_                        => expression,
					};
				}

				return expression;
			}

			private Expr BuildDynamicMemberName(GS2Parser.ExpressionContext context)
			{
				var expression = BuildExpression(context);
				return context.Start.Type == GS2Parser.CONCAT ? new StringCastExpr(RemoveLeadingStringCast(expression)) : expression;
			}

			private static Expr RemoveLeadingStringCast(Expr expression) =>
				expression switch
				{
					StringCastExpr cast                                         => cast.Expression,
					BinaryExpr { Left: var left, Op: var op, Right: var right } => new BinaryExpr(RemoveLeadingStringCast(left), op, right),
					_                                                           => expression,
				};

			private Expr BuildPrimary(GS2Parser.PrimaryExpressionContext context)
			{
				if (context.NUMBER() != null) return new NumberExpr(context.NUMBER().GetText());
				if (context.STRING() != null)
				{
					var text = context.STRING().GetText();
					if (text == "\\") throw Failure(context);
					return new StringExpr(text);
				}

				if (context.TRUE() != null) return new BoolExpr(true);
				if (context.FALSE() != null) return new BoolExpr(false);
				if (context.NULL() != null) return new NullExpr();
				if (context.castExpression() != null) return BuildCast(context.castExpression());
				if (context.IDENTIFIER() != null) return BuildIdentifier(context.IDENTIFIER().GetText());
				if (context.arrayLiteral() != null) return new ArrayLiteralExpr(BuildArguments(context.arrayLiteral().argumentList()));
				if (context.lambdaExpression() != null) return BuildLambda(context.lambdaExpression());
				if (context.newExpression() != null) return BuildNewExpression(context.newExpression());
				if (context.expression() != null) return BuildExpression(context.expression());
				throw Failure(context);
			}

			private static Expr BuildIdentifier(string text)
			{
				var keyCode = text switch { "VK_LEFT" => "37", "VK_UP" => "38", "VK_RIGHT" => "39", "VK_DOWN" => "40", "VK_DELETE" => "46", _ => null };
				if (keyCode != null) return new NumberExpr(keyCode);
				if (text is "playerx" or "playery" or "playerz" or "playerdir" or "playerreading") return new MemberExpr(new IdentifierExpr("player"), text[6..]);
				var parts = text.Split("::", StringSplitOptions.None);
				return parts.Length == 2 && !parts[0].StartsWith('$') ? new EnumExpr(parts[0], parts[1]) : new IdentifierExpr(text);
			}

			private CastExpr BuildCast(GS2Parser.CastExpressionContext context)
			{
				var type = context.INT_CAST() != null ? "int" : context.FLOAT_CAST() != null ? "float" : "_";
				return new(type, BuildExpression(context.expression()));
			}

			private LambdaExpr BuildLambda(GS2Parser.LambdaExpressionContext context) => new($"function_100_{_lambdaId++}", BuildArguments(context.argumentList()), BuildStatementBody(context.statement()));

			private Expr BuildNewExpression(GS2Parser.NewExpressionContext context) =>
				context.IDENTIFIER() != null ? new NewObjectExpr(context.IDENTIFIER().GetText(), BuildArguments(context.argumentList())) : new NewArrayExpr(context.arrayRank().Select(rank => BuildExpression(rank.expression())).ToList());

			private static Expr ApplyIndices(Expr expression, List<Expr> indices, bool zeroWhenEmpty) =>
				indices.Count switch
				{
					0 => zeroWhenEmpty ? new ArrayIndexExpr(expression, new NumberExpr("0")) : expression,
					1 => new ArrayIndexExpr(expression, indices[0]),
					_ => new MultiArrayIndexExpr(expression, indices),
				};

			private List<Expr> BuildArguments(GS2Parser.ArgumentListContext? context) => context == null ? [] : context.expression().Select(BuildExpression).ToList();

			private static Expr BuildBinaryChain<TContext>(ParserRuleContext context, TContext[] operands, Func<TContext, Expr> buildOperand) where TContext : ParserRuleContext
			{
				var expression = buildOperand(operands[0]);
				var operators  = OperatorTexts(context);
				for (var i = 1; i < operands.Length; ++i)
				{
					var op    = NormalizeOperator(operators[i - 1]);
					var right = buildOperand(operands[i]);
					if (expression is BoolExpr boolean && (op == "||" && boolean.Value || op == "&&" && !boolean.Value)) continue;
					if (op == "@" && expression is StringExpr leftString && right is StringExpr rightString)
						expression = new StringExpr(leftString.Value + rightString.Value);
					else
					{
						var x = expression is IdentifierExpr { Name: "pi" } ? Math.PI : expression is NumberExpr leftNumber ? double.Parse(leftNumber.Text, CultureInfo.InvariantCulture) : double.NaN;
						var y = right is IdentifierExpr { Name: "pi" }      ? Math.PI : right is NumberExpr rightNumber     ? double.Parse(rightNumber.Text, CultureInfo.InvariantCulture) : double.NaN;
						var value = op switch
						{
							"+"             => x + y,
							"-"             => x - y,
							"*"             => x * y,
							"/" when y != 0 => x / y,
							"%" when y != 0 => x % y,
							"^"             => Math.Pow(x, y),
							"&"             => (int)x & (int)y,
							"|"             => (int)x | (int)y,
							"xor"           => (int)x ^ (int)y,
							"<<"            => (int)x << (int)y,
							">>"            => (int)x >> (int)y,
							_               => double.NaN,
						};
						expression = double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(value) ? new NumberExpr(value.ToString("G", CultureInfo.InvariantCulture)) : new BinaryExpr(expression, op, right);
					}
				}

				return expression;
			}

			private static List<string> OperatorTexts(ParserRuleContext context)
			{
				List<string> operators = [];
				for (var i = 0; i < context.ChildCount; ++i)
					if (context.GetChild(i) is ITerminalNode terminal && IsBinaryOperator(terminal.Symbol.Type))
						operators.Add(terminal.GetText());
				return operators;
			}

			private static bool IsBinaryOperator(int type) =>
				type is GS2Parser.OR or GS2Parser.AND or GS2Parser.BAND or GS2Parser.BOR or GS2Parser.BXOR or GS2Parser.SHL or GS2Parser.SHR or GS2Parser.EQ or GS2Parser.NEQ or GS2Parser.NEQ_ALT or GS2Parser.LT or GS2Parser.LTE or GS2Parser.LTE_ALT
					or GS2Parser.GT or GS2Parser.GTE or GS2Parser.GTE_ALT or GS2Parser.CONCAT or GS2Parser.PLUS or GS2Parser.MINUS or GS2Parser.MUL or GS2Parser.DIV or GS2Parser.MOD or GS2Parser.POW;

			private static ParseFailure Failure(ParserRuleContext context) => new(context.Start.Line);
		}
	}

	private static class AntlrGs1Parser
	{
		public static ParseResult Parse(string code)
		{
			try
			{
				var input    = CharStreams.fromString(code);
				var lexer    = new GS1Lexer(input);
				var tokens   = new CommonTokenStream(lexer);
				var parser   = new GS1Parser(tokens);
				var listener = new ThrowingErrorListener();
				lexer.RemoveErrorListeners();
				parser.RemoveErrorListeners();
				lexer.AddErrorListener(listener);
				parser.AddErrorListener(listener);
				var program = parser.program();
				return parser.NumberOfSyntaxErrors == 0 ? new(new Builder(code).Build(program), null) : new(null, ErrorMessage(code, 1));
			}
			catch (ParseFailure ex)
			{
				return new(null, ErrorMessage(code, ex.Line));
			}
		}

		private sealed class Builder
		{
			private readonly bool _foldConditions;
			private          bool _comparisonContext;

			public Builder(string code, bool foldConditions = true)
			{
				_               = code;
				_foldConditions = foldConditions;
			}

			public ProgramNode Build(GS1Parser.ProgramContext context)
			{
				List<ProgramItem> items         = [];
				var               gs1EventFlags = 0;
				var               eventBuilder  = new Builder("", false);
				foreach (var block in context.block())
				{
					if (SingleStatement(block)?.functionDefinition() is { } functionDefinition) items.Add(new FunctionItem(BuildFunction(functionDefinition)));
					else
					{
						var statement = new BlockStmt(BuildBlock(block));
						// Official event flags include references removed by constant-condition folding.
						gs1EventFlags |= Gs1EventFlagScanner.Scan(new BlockStmt(eventBuilder.BuildBlock(block)));
						items.Add(new StatementItem(statement));
					}
				}

				return new([], [], items, gs1EventFlags);
			}

			private static GS1Parser.StatementContext? SingleStatement(GS1Parser.BlockContext context) => context.statement().Length == 1 && context.GetChild(0) is GS1Parser.StatementContext ? context.statement(0) : null;

			private FunctionNode BuildFunction(GS1Parser.FunctionDefinitionContext context)
			{
				if (context is not GS1Parser.StatementFunctionDefinitionContext functionDefinition) throw Failure(context);
				var (name, objectName) = BuildFunctionName(functionDefinition.compound_identifier().GetText());
				return new(name, objectName, false, [], BuildBlock(functionDefinition.block()));
			}

			private static (string Name, string? ObjectName) BuildFunctionName(string text)
			{
				var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
				return parts.Length <= 1 ? (text, null) : (parts[^1], string.Join('.', parts.Take(parts.Length - 1)));
			}

			private List<Stmt> BuildBlock(GS1Parser.BlockContext context)
			{
				List<Stmt> statements = [];
				foreach (var statement in context.statement())
				{
					var built = BuildStatement(statement);
					if (built is BlockStmt { Body.Count: 0 }) continue;
					statements.Add(built);
				}

				return statements;
			}

			private Stmt BuildStatement(GS1Parser.StatementContext context)
			{
				if (IsEmptyStatement(context)) return new BlockStmt([]);
				if (context.ifStatement() is { } ifStatement) return BuildIf(ifStatement);
				if (context.forStatement() is { } forStatement) return BuildFor(forStatement);
				if (context.whileStatement() is { } whileStatement) return BuildWhile(whileStatement);
				if (context.withStatement() is { } withStatement) return BuildWith(withStatement);
				if (context.functionDefinition() is { } functionDefinition) return new ExprStmt(new CallExpr(BuildFunction(functionDefinition).Name, []));
				if (context.flowStatement() is { } flowStatement) return BuildFlow(flowStatement);
				if (context.builtinCommandStatement() is { } commandStatement) return BuildCommand(commandStatement);
				if (context.userFunctionStatement() is { } userFunctionStatement) return BuildUserFunctionCall(userFunctionStatement);
				if (context.assignmentStatement() is { } assignmentStatement) return new ExprStmt(BuildAssignmentStatement(assignmentStatement));
				if (context.expression() is { } expression) return new ExprStmt(BuildExpression(expression));
				throw Failure(context);
			}

			private static bool IsEmptyStatement(GS1Parser.StatementContext context) => context.END() != null && context.ChildCount == 1;

			private Stmt BuildIf(GS1Parser.IfStatementContext context)
			{
				if (context is not GS1Parser.StatementIfContext ifStatement) throw Failure(context);
				var blocks    = ifStatement.block();
				var hasElse   = blocks.Length > 1;
				var condition = BuildCondition(ifStatement.expression());
				if (_foldConditions && ConstantTruth(condition) is { } truth) return new BlockStmt(truth ? BuildBlock(blocks[0]) : hasElse ? BuildBlock(blocks[1]) : []);
				return new IfStmt(condition, BuildBlock(blocks[0]), hasElse ? BuildBlock(blocks[1]) : [], hasElse);
			}

			private ForStmt BuildFor(GS1Parser.ForStatementContext context)
			{
				if (context is not GS1Parser.StatementForContext forStatement) throw Failure(context);
				var   assignments     = forStatement.assignmentStatement();
				var   expressions     = forStatement.expression();
				Expr? init            = null;
				Expr? post            = null;
				var   expressionIndex = 0;
				if (assignments.Length > 0 && assignments[0].Start.TokenIndex < forStatement.END(0).Symbol.TokenIndex) init                                  = BuildAssignmentStatement(assignments[0]);
				else if (expressions.Length > expressionIndex && expressions[expressionIndex].Start.TokenIndex < forStatement.END(0).Symbol.TokenIndex) init = BuildExpression(expressions[expressionIndex++]);
				var condition = expressions.Length > expressionIndex && expressions[expressionIndex].Start.TokenIndex < forStatement.END(1).Symbol.TokenIndex ? BuildExpression(expressions[expressionIndex++]) : new BoolExpr(true);
				if (assignments.Length > 0 && assignments[^1].Start.TokenIndex > forStatement.END(1).Symbol.TokenIndex) post = BuildAssignmentStatement(assignments[^1]);
				else if (expressions.Length > expressionIndex) post                                                          = BuildExpression(expressions[expressionIndex]);
				return new(init, condition, post, BuildBlock(forStatement.block()));
			}

			private WhileStmt BuildWhile(GS1Parser.WhileStatementContext context)
			{
				if (context is not GS1Parser.StatementWhileContext whileStatement) throw Failure(context);
				return new(BuildCondition(whileStatement.expression()), BuildBlock(whileStatement.block()));
			}

			private Stmt BuildWith(GS1Parser.WithStatementContext context)
			{
				if (context is not GS1Parser.StatementWithContext withStatement) throw Failure(context);
				return new WithStmt(BuildExpression(withStatement.expression()), BuildBlock(withStatement.block()));
			}

			private static Stmt BuildFlow(GS1Parser.FlowStatementContext context) =>
				context switch
				{
					GS1Parser.FlowReturnContext   => new ReturnStmt(new NumberExpr("0")),
					GS1Parser.FlowBreakContext    => new BreakStmt(),
					GS1Parser.FlowContinueContext => new ContinueStmt(),
					_                             => throw Failure(context),
				};

			private BinaryExpr BuildAssignmentStatement(GS1Parser.AssignmentStatementContext context)
			{
				if (context is not GS1Parser.StatementAssignmentContext assignment) throw Failure(context);
				return new(BuildIdentifierAccess(assignment.identifier_access()), NormalizeOperator(assignment.assignment_operator().GetText()), BuildExpression(assignment.expression()));
			}

			private ExprStmt BuildCommand(GS1Parser.BuiltinCommandStatementContext context)
			{
				if (context is not GS1Parser.StatementBuiltInCommandContext command) throw Failure(context);
				var name                                                                           = command.COMMAND().GetText().Trim();
				var args                                                                           = command.builtInCommandExpression().Select(BuildBuiltInCommandExpression).ToList();
				var parameterStart = name switch { "triggeraction" => 3, "setshootparams" => 0, _ => args.Count };
				for (var i = 0; i < args.Count && i <= parameterStart; ++i) args[i] = TrimArgument(args[i]);
				if (name is "set" or "unset" && args.Count == 1)
				{
					var target = new DynamicVarExpr(new StringExpr(command.builtInCommandExpression(0).GetText().Trim()));
					return new(new BinaryExpr(target, "=", name == "set" ? new NumberExpr("1") : new StringExpr("")));
				}

				if (name == "setstring" && args.Count == 2)
				{
					var target = new DynamicVarExpr(BuildText(command.builtInCommandExpression(0)));
					return new(new BinaryExpr(target, "=", AsString(args[1]) with { IsGs1Embedded = true }));
				}

				if (name == "setplayerprop" && args.Count == 2)
				{
					var target = BuildText(command.builtInCommandExpression(0));
					if (target is MemberExpr or ArrayIndexExpr) return new(new BinaryExpr(target, "=", AsString(args[1])) { IsGs1Embedded = true });
				}

				if (name == "tokenize" && args.Count == 1)
					return new(new BinaryExpr(new IdentifierExpr("tokens"), "=", new MethodCallExpr(args[0], "tokenize", []) { IsGs1Embedded = true }));
				if (name == "setcharprop" && args.Count == 2 && BuildText(command.builtInCommandExpression(0)) is MemberExpr { Object: IdentifierExpr { Name: "player" } } characterProperty)
					return new(new BinaryExpr(new IdentifierExpr(characterProperty.Name), "=", AsString(args[1])) { IsGs1Embedded = true });
				if (name is "addstring" or "deletestring" && args.Count == 2)
					return new(new MethodCallExpr(new DynamicVarExpr(BuildText(command.builtInCommandExpression(0))), name == "addstring" ? "add" : "delete", [name == "addstring" ? AsString(args[1]) : new CastExpr("float", args[1])]));
				if (name == "replacestring" && args.Count == 3)
					return new(new MethodCallExpr(new DynamicVarExpr(BuildText(command.builtInCommandExpression(0))), "replace", [new CastExpr("float", args[1]), AsString(args[2])]) { IsGs1Embedded = true });
				if (name == "removestring" && args.Count == 2)
					return new(new MethodCallExpr(new DynamicVarExpr(BuildText(command.builtInCommandExpression(0))), "remove", [AsString(args[1])]) { IsGs1Embedded = true });
				if (args.Count > parameterStart + 1)
				{
					var parameters = args[parameterStart];
					foreach (var argument in args.Skip(parameterStart + 1)) parameters = AppendParameterText(AppendText(parameters, new StringExpr(",")), argument);
					args = [.. args.Take(parameterStart), parameters];
				}

				return new(new CallExpr(name, args));
			}

			private static Expr AsString(Expr expression) => expression is StringExpr or StringCastExpr or BinaryExpr { Op: "@" } ? expression : new StringCastExpr(expression);

			// The grammar splits comma-separated parameters, but GS1 passes one interpolated string.
			[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
			private static Expr AppendParameterText(Expr left, Expr right) => right is BinaryExpr { Op: "@" } text
				? AppendParameterText(AppendParameterText(left, text.Left), text.Right)
				: AppendText(left, right);

			private static Expr TrimArgument(Expr expression) =>
				expression switch
				{
					StringExpr str                                           => new StringExpr(str.Value.TrimStart(' ', '\t')),
					BinaryExpr { Op: "@", Left: var left, Right: var right } => TrimArgument(left) is StringExpr { Value.Length: 0 } ? right : new BinaryExpr(TrimArgument(left), "@", right),
					_                                                        => expression,
				};

			private Expr BuildBuiltInCommandExpression(GS1Parser.BuiltInCommandExpressionContext context)
			{
				if (context.special_literal() is { } specialLiteral) return BuildSpecialLiteral(specialLiteral);
				if (context.expression() is { } expression) return BuildExpression(expression);
				throw Failure(context);
			}

			private ExprStmt BuildUserFunctionCall(GS1Parser.UserFunctionStatementContext context)
			{
				if (context is not GS1Parser.StatementUserFunctionCallContext call) throw Failure(context);
				return new(new CallExpr(call.compound_identifier().GetText(), []));
			}

			[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
			private Expr BuildCondition(GS1Parser.ExpressionContext context)
			{
				var comparisonContext = _comparisonContext;
				_comparisonContext = true;
				try
				{
					return AsBooleanOperand(BuildExpression(context));
				}
				finally
				{
					_comparisonContext = comparisonContext;
				}
			}

			[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
			private static Expr AsBooleanOperand(Expr expression) => expression is IdentifierExpr { Name: "timeout" } ? new IdentifierExpr("istimeout") : expression;

			private Expr BuildExpression(GS1Parser.ExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionTernaryContext ternary) throw Failure(context);
				if (ternary.legacyEqualityExpression() is not GS1Parser.ExpressionLegacyEqualityContext equality) throw Failure(context);
				var    comparisonContext = _comparisonContext;
				var    operandContexts   = equality.logicalOrExpression();
				Expr[] operands;
				if (operandContexts.Length > 1) _comparisonContext = false;
				try
				{
					operands = operandContexts.Select(BuildLogicalOr).ToArray();
				}
				finally
				{
					_comparisonContext = comparisonContext;
				}

				var expression = operands[0];
				if (operands.Length > 1)
				{
					var right                                           = operands[^1];
					for (var i = operands.Length - 2; i > 0; --i) right = new BinaryExpr(operands[i], "=", right);
					expression = new BinaryExpr(expression, comparisonContext ? "==" : "=", right);
				}

				var branches = ternary.expression();
				for (var i = 0; i + 1 < branches.Length; i += 2)
					expression = new TernaryExpr(AsBooleanOperand(expression), BuildExpression(branches[i]), BuildExpression(branches[i + 1]));
				return expression;
			}

			private Expr BuildLogicalOr(GS1Parser.LogicalOrExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionLogicOrContext logicalOr) throw Failure(context);
				return BuildBinaryChain(logicalOr, logicalOr.logicalAndExpression(), BuildLogicalAnd);
			}

			private Expr BuildLogicalAnd(GS1Parser.LogicalAndExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionLogicAndContext logicalAnd) throw Failure(context);
				return BuildBinaryChain(logicalAnd, logicalAnd.equalityExpression(), BuildEquality);
			}

			private Expr BuildEquality(GS1Parser.EqualityExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionEqualityContext equality) throw Failure(context);
				return BuildBinaryChain(equality, equality.relationalExpression(), BuildRelational);
			}

			private Expr BuildRelational(GS1Parser.RelationalExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionRelationalContext relational) throw Failure(context);
				return BuildBinaryChain(relational, relational.inExpression(), BuildIn);
			}

			private Expr BuildAdditive(GS1Parser.AdditiveExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionAdditiveContext additive) throw Failure(context);
				return BuildBinaryChildren(
					additive,
					child => child switch
					{
						GS1Parser.MultiplicativeExpressionContext multiplicative => BuildMultiplicative(multiplicative),
						ITerminalNode { Symbol.Type: GS1Parser.LITERAL } literal => BuildLiteral(literal.GetText()),
						_                                                        => null,
					}
				);
			}

			private Expr BuildMultiplicative(GS1Parser.MultiplicativeExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionMultiplicativeContext multiplicative) throw Failure(context);
				return BuildBinaryChildren(
					multiplicative,
					child => child switch
					{
						GS1Parser.ModuloExpressionContext modulo                 => BuildModulo(modulo),
						ITerminalNode { Symbol.Type: GS1Parser.LITERAL } literal => BuildLiteral(literal.GetText()),
						_                                                        => null,
					}
				);
			}

			private Expr BuildModulo(GS1Parser.ModuloExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionModuloContext modulo) throw Failure(context);
				return BuildBinaryChildren(
					modulo,
					child => child switch
					{
						GS1Parser.ExponentiationExpressionContext exponentiation => BuildExponentiation(exponentiation),
						ITerminalNode { Symbol.Type: GS1Parser.LITERAL } literal => BuildLiteral(literal.GetText()),
						_                                                        => null,
					}
				);
			}

			private Expr BuildIn(GS1Parser.InExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionInContext inExpression) throw Failure(context);
				var  subjects = inExpression.additiveExpression().Select(BuildAdditive).ToList();
				Expr left     = subjects.Count == 1 ? subjects[0] : new ArrayLiteralExpr(subjects);
				if (inExpression.OP_IN() == null) return left;
				if (inExpression.range_literal() is { } rangeLiteral)
				{
					if (rangeLiteral is not GS1Parser.RangeLiteralContext range) throw Failure(rangeLiteral);
					var expressions = range.expression();
					return new InExpr(left, BuildExpression(expressions[0]), BuildExpression(expressions[1])) { LowerExclusive = range.OP_LESS() != null, UpperExclusive = range.OP_GREAT() != null };
				}

				return new InExpr(left, BuildPrimary(inExpression.primaryExpression()), null);
			}

			private Expr BuildExponentiation(GS1Parser.ExponentiationExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionExponentiationContext exponentiation) throw Failure(context);
				return BuildBinaryChain(exponentiation, exponentiation.unaryExpression(), BuildUnary);
			}

			private Expr BuildUnary(GS1Parser.UnaryExpressionContext context)
			{
				if (context is GS1Parser.IgnoreExpressionUnaryPrimaryContext primary) return BuildPostfix(primary.postfixExpression());
				if (context is not GS1Parser.ExpressionUnaryContext unary) throw Failure(context);
				var op                    = unary.GetChild(0).GetText();
				var expression            = BuildUnary(unary.unaryExpression());
				if (op == "!") expression = AsBooleanOperand(expression);
				if (op == "-" && expression is BoolExpr boolean) return new NumberExpr(boolean.Value ? "-1" : "0");
				if (op == "-" && expression is NumberExpr number)
					return new NumberExpr((-double.Parse(number.Text, CultureInfo.InvariantCulture)).ToString("G", CultureInfo.InvariantCulture));
				return op == "+" ? expression : new UnaryExpr(op, expression);
			}

			private Expr BuildPostfix(GS1Parser.PostfixExpressionContext context)
			{
				if (context is GS1Parser.IgnoreExpressionPostfixPrimaryContext primary) return BuildPrimary(primary.primaryExpression());
				if (context is not GS1Parser.ExpressionPostfixContext postfix) throw Failure(context);
				return new UnaryExpr(postfix.GetChild(1).GetText(), BuildPrimary(postfix.primaryExpression()), true);
			}

			private Expr BuildPrimary(GS1Parser.PrimaryExpressionContext context)
			{
				if (context.expression() is { } expression) return BuildExpression(expression);
				if (context.RAWMESSAGECODE() != null) return new StringExpr(context.GetText());
				if (context.builtin_function() is { } builtInFunction) return BuildBuiltInFunction(builtInFunction);
				if (context.array_literal() is { } arrayLiteral) return BuildArrayLiteral(arrayLiteral);
				if (context.literal_literal() is { } literalLiteral) return BuildLiteralLiteral(literalLiteral);
				if (context.identifier_access() is { } identifierAccess) return BuildIdentifierAccess(identifierAccess);
				if (context.compound_string() is { } compoundString) return BuildCompoundString(compoundString);
				throw Failure(context);
			}

			private Expr BuildBuiltInFunction(GS1Parser.Builtin_functionContext context)
			{
				if (context is not GS1Parser.BuiltInFunctionCallContext call) throw Failure(context);
				var args = call.expression().Select(BuildExpression).Select(TrimArgument).ToList();
				if (call.FUNCTION().GetText() == "playersays")
				{
					for (var i = 0; i < args.Count; ++i)
					{
						var text = call.expression(i).GetText().Trim();
						if (text.StartsWith('"'))
						{
							var lexer                                                                                    = new GS2Lexer(CharStreams.fromString(text));
							var literal                                                                                  = lexer.NextToken();
							if (literal.Type == GS2Lexer.STRING && lexer.NextToken().Type == TokenConstants.EOF) args[i] = new StringExpr(literal.Text);
							continue;
						}

						try
						{
							// Parentheses select expression lexing even for names shared with commands.
							var lexer    = new GS1Lexer(CharStreams.fromString("(" + text + ")"));
							var tokens   = new CommonTokenStream(lexer);
							var parser   = new GS1Parser(tokens);
							var listener = new ThrowingErrorListener();
							lexer.RemoveErrorListeners();
							parser.RemoveErrorListeners();
							lexer.AddErrorListener(listener);
							parser.AddErrorListener(listener);
							var expression = parser.expression();
							if (tokens.LA(1) != TokenConstants.EOF) continue;
							var terminals = tokens.GetTokens().Where(token => token.Channel == TokenConstants.DefaultChannel).ToArray();
							// Do not merge free-text words into a compound identifier.
							if (terminals.Zip(terminals.Skip(1)).Any(pair => pair.First.Type == GS1Lexer.IDENTIFIER && pair.Second.Type == GS1Lexer.IDENTIFIER)) continue;
							args[i] = BuildExpression(expression);
						}
						catch (ParseFailure)
						{
							// Unquoted phrases remain a supported local extension.
						}
					}
				}

				if (call.FUNCTION().GetText() == "strequals" && args.Count == 2)
				{
					return new BinaryExpr(args[0], "==", args[1]);
				}

				if (call.FUNCTION().GetText() == "hasweapon") return new BinaryExpr(new CallExpr("findweapon", args), "!=", new NullExpr());
				if (call.FUNCTION().GetText() == "strlen" && args.Count == 1) return new MethodCallExpr(args[0], "length", []) { IsGs1Embedded                      = true };
				if (call.FUNCTION().GetText() == "startswith" && args.Count == 2) return new MethodCallExpr(args[1], "starts", [AsString(args[0])]) { IsGs1Embedded = true };
				if (call.FUNCTION().GetText() == "strcontains" && args.Count == 2) return new CallExpr("contains", args);
				if (call.FUNCTION().GetText() == "indexof" && args.Count == 2) return new MethodCallExpr(args[1], "pos", [args[0]]) { IsGs1Embedded = true };
				if (call.FUNCTION().GetText() == "lindexof" && args.Count == 2) return new MethodCallExpr(new DynamicVarExpr(BuildText(call.expression(1))), "index", [args[0]]);
				if (call.FUNCTION().GetText() == "sarraylen" && args.Count == 1) return new CallExpr("arraylen", [new DynamicVarExpr(BuildText(call.expression(0)))]);
				var name = NormalizeBuiltInFunctionName(call.FUNCTION().GetText());
				return new CallExpr(name, args);
			}

			private static string NormalizeBuiltInFunctionName(string name) =>
				name switch
				{
					"hasweapon" => "findweapon",
					"onmapx"    => "getmapx",
					"onmapy"    => "getmapy",
					"imgwidth"  => "getimgwidth",
					"imgheight" => "getimgheight",
					"ascii"     => "getascii",
					"keycode"   => "getkeycode",
					_           => name,
				};

			private Expr BuildIdentifierAccess(GS1Parser.Identifier_accessContext context)
			{
				if (context is not GS1Parser.IdentifierAccessContext access) throw Failure(context);
				var values = access.identifier_value();
				var (name, indices) = BuildIdentifierValue(values[0]);
				var identifier = ((GS1Parser.IdentifierValueContext)values[0]).compound_identifier();
				var expression = identifier.ChildCount == 1 && identifier.GetChild(0) is GS1Parser.MessageCodeContext message ? ApplyIndices(BuildMessageCode(message), indices) : BuildDottedIdentifier(name, indices);
				for (var i = 1; i < values.Length; ++i)
				{
					(name, indices) = BuildIdentifierValue(values[i]);
					expression      = BuildDottedMember(expression, name, indices);
				}

				return expression;
			}

			private (string Name, List<Expr> Indices) BuildIdentifierValue(GS1Parser.Identifier_valueContext context)
			{
				if (context is not GS1Parser.IdentifierValueContext value) throw Failure(context);
				return (value.compound_identifier().GetText(), value.expression().Select(BuildExpression).ToList());
			}

			private static Expr BuildDottedIdentifier(string name, List<Expr> indices)
			{
				var playerMember = name switch
				{
					"playerx"           => "x",
					"playery"           => "y",
					"playerz"           => "z",
					"playerdir"         => "dir",
					"playersprite"      => "sprite",
					"playerswordpower"  => "swordpower",
					"playershieldpower" => "shieldpower",
					"playerglovepower"  => "glovepower",
					"playerbombs"       => "bombs",
					"playerdarts"       => "darts",
					"playerhearts"      => "hearts",
					"playerhurtpower"   => "hurtpower",
					"playerfullhearts"  => "fullhearts",
					"playerrupees"      => "rupees",
					"playerap"          => "ap",
					"playerid"          => "id",
					"playerattachid"    => "attachid",
					"playerpaused"      => "paused",
					"playermp"          => "mp",
					"playerfreezetime"  => "freezetime",
					_                   => null,
				};
				if (playerMember != null) return ApplyIndices(new MemberExpr(new IdentifierExpr("player"), playerMember), indices);
				if (name is "npcscount" or "compuscount" or "playerscount" or "bombscount" or "itemscount" or "tokenscount" or "arrowscount" or "exploscount")
					return new CallExpr("arraylen", [new IdentifierExpr(name[..^5])]);
				if (name == "weaponscount") return new CallExpr("arraylen", [new MemberExpr(new IdentifierExpr("player"), "weapons")]);
				var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length == 0) return ApplyIndices(new IdentifierExpr(name), indices);
				Expr expression                                   = new IdentifierExpr(parts[0]);
				for (var i = 1; i < parts.Length; ++i) expression = new MemberExpr(expression, parts[i]);
				return ApplyIndices(expression, indices);
			}

			private static Expr BuildDottedMember(Expr expression, string name, List<Expr> indices)
			{
				var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length == 0) return ApplyIndices(new MemberExpr(expression, name), indices);
				foreach (var part in parts) expression = new MemberExpr(expression, part);
				return ApplyIndices(expression, indices);
			}

			private static Expr ApplyIndices(Expr expression, List<Expr> indices) =>
				indices.Count switch
				{
					0 => expression,
					1 => new ArrayIndexExpr(expression, indices[0]),
					_ => new MultiArrayIndexExpr(expression, indices),
				};

			private ArrayLiteralExpr BuildArrayLiteral(GS1Parser.Array_literalContext context)
			{
				if (context is not GS1Parser.ArrayLiteralContext arrayLiteral) throw Failure(context);
				List<Expr> values         = [];
				var        expectingValue = true;
				for (var i = 1; i < arrayLiteral.ChildCount - 1; ++i)
				{
					var child = arrayLiteral.GetChild(i);
					if (child is ITerminalNode terminal && terminal.Symbol.Type == GS1Parser.TOKEN_COMMA)
					{
						if (expectingValue) values.Add(new NumberExpr("0"));
						expectingValue = true;
					}
					else if (child is GS1Parser.ExpressionContext expression)
					{
						values.Add(BuildExpression(expression));
						expectingValue = false;
					}
				}

				return new(values);
			}

			private static Expr BuildLiteralLiteral(GS1Parser.Literal_literalContext context) => BuildLiteral(context.GetText());

			private static Expr BuildSpecialLiteral(GS1Parser.Special_literalContext context) =>
				context switch
				{
					GS1Parser.DirectionLiteralContext => DirectionExpr(context.GetText()),
					_                                 => new StringExpr(context.GetText()),
				};

			private Expr BuildCompoundString(GS1Parser.Compound_stringContext context) => BuildText(context);

			private Expr BuildText(IParseTree context)
			{
				if (context is GS1Parser.MessageCodeContext message) return BuildMessageCode(message);
				if (context is ITerminalNode token) return new StringExpr(token.GetText().Replace("\r", "").Replace("\n", ""));
				Expr result = new StringExpr("");
				for (var i = 0; i < context.ChildCount; i++)
				{
					var next = BuildText(context.GetChild(i));
					if (context.GetChild(i) is GS1Parser.MessageCodeContext && next is StringExpr literal && literal.Value.StartsWith('#'))
					{
						// Literal message codes keep the native hash/text boundary after interpolation.
						result = AppendText(result, new StringExpr("#"));
						var tail = literal.Value[1..];
						if (i + 1 < context.ChildCount && context.GetChild(i + 1) is ITerminalNode { Symbol.Type: GS1Parser.STRING } following)
						{
							tail += following.GetText().Replace("\r", "").Replace("\n", "");
							++i;
						}

						next = new StringExpr(tail);
					}

					if (result is not StringExpr && next is StringExpr text)
					{
						// Native interpolation retains each literal brace as a separate operand.
						var start = 0;
						for (var position = 0; position < text.Value.Length; ++position)
						{
							if (text.Value[position] is not ('{' or '}')) continue;
							result = AppendText(result, new StringExpr(text.Value[start..position]));
							result = AppendText(result, new StringExpr(text.Value[position].ToString()));
							start  = position + 1;
						}

						next = new StringExpr(text.Value[start..]);
					}

					result = AppendText(result, next);
				}

				return result;
			}

			[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
			private static Expr AppendText(Expr left, Expr right) =>
				(left, right) switch
				{
					(_, StringExpr { Value.Length: 0 }) => left,
					(StringExpr { Value.Length: 0 }, _) => right,
					(StringExpr a, StringExpr b)        => new StringExpr(a.Value + b.Value),
					_                                   => new BinaryExpr(left, "@", right),
				};

			private Expr BuildMessageCode(GS1Parser.MessageCodeContext context)
			{
				var  code      = context.MESSAGECODE().GetText();
				var  arguments = context.expression();
				Expr player    = arguments.Length == 1 ? new ArrayIndexExpr(new IdentifierExpr("players"), BuildExpression(arguments[0])) : new IdentifierExpr("player");
				var member = code switch
				{
					"#a" => "account",
					"#n" => "nick",
					"#c" => "chat",
					"#g" => "guild",
					"#m" => "ani",
					"#1" => "swordimg",
					"#2" => "shieldimg",
					"#3" => "headimg",
					"#5" => "horseimg",
					"#6" => "carrynpc",
					"#8" => "bodyimg",
					_    => null,
				};
				if (member != null) return new MemberExpr(player, member);
				if (code == "#f") return new IdentifierExpr("image") { IsGs1Embedded = true };
				if (code == "#D" && arguments.Length == 0) return new IdentifierExpr("downloadfile");
				if (code == "#N" && arguments.Length == 0) return new IdentifierExpr("name");
				if (code == "#N" && arguments.Length == 1) return new MemberExpr(new ArrayIndexExpr(new IdentifierExpr("npcs"), BuildExpression(arguments[0])), "name");
				if (code == "#F" && arguments.Length == 0) return new MemberExpr(new IdentifierExpr("level"), "name");
				if (code == "#L") return new MemberExpr(new MemberExpr(player, "level"), "name");
				if (code is "#w" or "#W")
				{
					Expr weapon = arguments.Length == 1 ? new ArrayIndexExpr(new MemberExpr(new IdentifierExpr("player"), "weapons"), BuildExpression(arguments[0])) : new MemberExpr(new IdentifierExpr("player"), "weapon");
					return new MemberExpr(weapon, code == "#W" ? "image" : "name");
				}

				if (code == "#s" && arguments.Length == 1) return new DynamicVarExpr(BuildText(arguments[0]));
				if (code == "#v" && arguments.Length == 1) return new CastExpr("float", BuildExpression(arguments[0]));
				if (code == "#p" && arguments.Length == 1) return new ArrayIndexExpr(new IdentifierExpr("params"), BuildExpression(arguments[0]));
				if (code == "#t" && arguments.Length == 1) return new ArrayIndexExpr(new IdentifierExpr("tokens"), BuildExpression(arguments[0]));
				if (code == "#I" && arguments.Length == 2) return new ArrayIndexExpr(new DynamicVarExpr(BuildText(arguments[0])), BuildExpression(arguments[1]));
				if (code == "#e" && arguments.Length == 3) return new MethodCallExpr(BuildExpression(arguments[2]), "substring", [BuildExpression(arguments[0]), BuildExpression(arguments[1])]) { IsGs1Embedded = true };
				if (code == "#T" && arguments.Length == 1) return new MethodCallExpr(TrimArgument(BuildExpression(arguments[0])), "trim", []) { IsGs1Embedded                                                    = true };
				if (code.StartsWith("#P", StringComparison.Ordinal) || code.StartsWith("#C", StringComparison.Ordinal))
				{
					Expr? index = int.TryParse(code.AsSpan(2), out var value) ? new NumberExpr(value.ToString(CultureInfo.InvariantCulture)) : arguments.Length == 1 ? BuildExpression(arguments[0]) : null;
					if (index != null) return new ArrayIndexExpr(new MemberExpr(code.Length > 2 ? player : new IdentifierExpr("player"), code[1] == 'P' ? "attr" : "colors"), index);
				}

				return new StringExpr(context.GetText());
			}

			private static Expr BuildLiteral(string text) =>
				text switch
				{
					"true"                              => new BoolExpr(true),
					"false"                             => new BoolExpr(false),
					"allfeatures" or "allstats" or "pi" => new IdentifierExpr(text),
					_                                   => Number(text),
				};

			private static NumberExpr Number(string text)
			{
				if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
					return new(Convert.ToInt32(text[2..], 16).ToString(CultureInfo.InvariantCulture));
				return new(text);
			}

			private static Expr DirectionExpr(string text) =>
				text switch
				{
					"up"    => new NumberExpr("0"),
					"left"  => new NumberExpr("1"),
					"down"  => new NumberExpr("2"),
					"right" => new NumberExpr("3"),
					_       => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? new NumberExpr((value % 4).ToString(CultureInfo.InvariantCulture)) : new StringExpr(text),
				};

			private Expr BuildBinaryChain<TContext>(ParserRuleContext context, TContext[] operands, Func<TContext, Expr> buildOperand) where TContext : ParserRuleContext
			{
				var expression = buildOperand(operands[0]);
				var operators  = OperatorTexts(context);
				for (var i = 1; i < operands.Length; ++i)
					expression = BuildNumericBinary(expression, NormalizeOperator(operators[i - 1]), buildOperand(operands[i]));
				return expression;
			}

			private Expr BuildBinaryChildren(ParserRuleContext context, Func<IParseTree, Expr?> buildOperand)
			{
				Expr?   expression = null;
				string? op         = null;
				for (var i = 0; i < context.ChildCount; ++i)
				{
					var child = context.GetChild(i);
					if (child is ITerminalNode terminal && IsBinaryOperator(terminal.Symbol.Type))
					{
						op = NormalizeOperator(terminal.GetText());
						continue;
					}

					var operand = buildOperand(child);
					if (operand == null) continue;
					if (expression == null) expression = operand;
					else expression                    = BuildNumericBinary(expression, op ?? throw Failure(context), operand);
					op = null;
				}

				return expression ?? throw Failure(context);
			}

			[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
			private static bool? ConstantTruth(Expr expression) =>
				expression switch
				{
					BoolExpr boolean                                                                                                     => boolean.Value,
					NumberExpr number when double.TryParse(number.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value != 0,
					_                                                                                                                    => null,
				};

			[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
			private Expr BuildNumericBinary(Expr left, string op, Expr right)
			{
				if (op is "||" or "&&")
				{
					left  = AsBooleanOperand(left);
					right = AsBooleanOperand(right);
				}

				if (_foldConditions && op is "||" or "&&")
				{
					var truth = ConstantTruth(left);
					if (truth == true && op == "||" || truth == false && op == "&&") return new BoolExpr(truth.Value);
				}

				var x = left is IdentifierExpr { Name: "pi" }  ? Math.PI : left is NumberExpr a && double.TryParse(a.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftValue)   ? leftValue : double.NaN;
				var y = right is IdentifierExpr { Name: "pi" } ? Math.PI : right is NumberExpr b && double.TryParse(b.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightValue) ? rightValue : double.NaN;
				if (double.IsFinite(x) && double.IsFinite(y))
				{
					var value = op switch
					{
						"+"             => x + y,
						"-"             => x - y,
						"*"             => x * y,
						"/" when y != 0 => x / y,
						"%" when y != 0 => x % y,
						"^"             => Math.Pow(x, y),
						_               => double.NaN,
					};
					if (double.IsFinite(value)) return new NumberExpr(value.ToString("G", CultureInfo.InvariantCulture));
				}

				return new BinaryExpr(left, op, right);
			}

			private static List<string> OperatorTexts(ParserRuleContext context)
			{
				List<string> operators = [];
				for (var i = 0; i < context.ChildCount; ++i)
					if (context.GetChild(i) is ITerminalNode terminal && IsBinaryOperator(terminal.Symbol.Type))
						operators.Add(terminal.Symbol.Type == GS1Parser.OP_ASSIGN ? "==" : NormalizeOperator(terminal.GetText()));
				return operators;
			}

			private static bool IsBinaryOperator(int type) =>
				type is GS1Parser.OP_LOGICALOR or GS1Parser.OP_LOGICALAND or GS1Parser.OP_EQUAL or GS1Parser.OP_ASSIGN or GS1Parser.OP_NOTEQ or GS1Parser.OP_LESS or GS1Parser.OP_GREAT or GS1Parser.OP_LESS_EQ or GS1Parser.OP_GREAT_EQ
					or GS1Parser.OP_ADD or GS1Parser.OP_SUB or GS1Parser.OP_MUL or GS1Parser.OP_DIV or GS1Parser.OP_MOD or GS1Parser.OP_POW;

			private static ParseFailure Failure(ParserRuleContext context) => new(context.Start.Line);
		}
	}

	private static class Gs1EventFlagScanner
	{
		private static readonly Dictionary<string, int> EventFlags = new(StringComparer.OrdinalIgnoreCase)
		{
			["playerenters"]      = 1 << 0,
			["playertouchsme"]    = 1 << 1,
			["playertouchsother"] = 1 << 2,
			["playerchats"]       = 1 << 3,
			["playerhurt"]        = 1 << 4,
			["playerdies"]        = 1 << 5,
			["playerlaysitem"]    = 1 << 6,
			["playerendsreading"] = 1 << 7,
			["compusdied"]        = 1 << 8,
			["emoticon"]          = 1 << 9,
			["mousedown"]         = 1 << 10,
			["mouseup"]           = 1 << 11,
			["mousewheel"]        = 1 << 12,
			["exploded"]          = 1 << 13,
			["wasshot"]           = 1 << 14,
			["waspelt"]           = 1 << 15,
			["keypressed"]        = 1 << 16,
			["actionprojectile2"] = 1 << 17,
			["playerleaves"]      = 1 << 21,
			["washit"]            = 1 << 22,
			["shapetrigger"]      = 1 << 25,
			["playanimation"]     = 1 << 26,
			["pkzonechanges"]     = 1 << 27,
		};

		public static int Scan(Stmt statement) => ScanStatement(statement);

		private static int ScanStatements(List<Stmt> statements)
		{
			var flags                                   = 0;
			foreach (var statement in statements) flags |= ScanStatement(statement);
			return flags;
		}

		private static int ScanStatement(Stmt statement) =>
			statement switch
			{
				ExprStmt exprStmt       => ScanExpression(exprStmt.Expression),
				InlineStmt inline       => ScanStatement(inline.Statement),
				BlockStmt block         => ScanStatements(block.Body),
				ReturnStmt returnStmt   => ScanExpression(returnStmt.Expression),
				IfStmt ifStmt           => ScanExpression(ifStmt.Condition) | ScanStatements(ifStmt.ThenBody) | ScanStatements(ifStmt.ElseBody),
				ForStmt forStmt         => ScanExpression(forStmt.Init) | ScanExpression(forStmt.Condition) | ScanExpression(forStmt.Post) | ScanStatements(forStmt.Body),
				ForEachStmt forEachStmt => ScanExpression(forEachStmt.Name) | ScanExpression(forEachStmt.Source) | ScanStatements(forEachStmt.Body),
				WhileStmt whileStmt     => ScanExpression(whileStmt.Condition) | ScanStatements(whileStmt.Body),
				WithStmt withStmt       => ScanExpression(withStmt.Target) | ScanStatements(withStmt.Body),
				SwitchStmt switchStmt   => ScanSwitch(switchStmt),
				NewStmt newStmt         => ScanExpressions(newStmt.Args) | ScanStatements(newStmt.Body),
				_                       => 0,
			};

		private static int ScanSwitch(SwitchStmt statement)
		{
			var flags = ScanExpression(statement.Expression);
			foreach (var switchCase in statement.Cases)
			{
				foreach (var label in switchCase.Labels) flags |= ScanExpression(label);
				flags |= ScanStatements(switchCase.Body);
			}

			return flags;
		}

		private static int ScanExpressions(IEnumerable<Expr> expressions)
		{
			var flags                                     = 0;
			foreach (var expression in expressions) flags |= ScanExpression(expression);
			return flags;
		}

		private static int ScanExpression(Expr? expression) =>
			expression switch
			{
				null                                                                                                    => 0,
				BinaryExpr { Op: "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "^=" or "@=" or "<<=" or ">>=" } binary => ScanExpression(binary.Right),
				BinaryExpr binary                                                                                       => ScanExpression(binary.Left) | ScanExpression(binary.Right),
				InExpr inExpr                                                                                           => ScanExpression(inExpr.Expression) | ScanExpression(inExpr.Lower) | ScanExpression(inExpr.Upper),
				TernaryExpr ternary                                                                                     => ScanExpression(ternary.Condition) | ScanExpression(ternary.WhenTrue) | ScanExpression(ternary.WhenFalse),
				UnaryExpr unary                                                                                         => ScanExpression(unary.Expression),
				StringCastExpr cast                                                                                     => ScanExpression(cast.Expression),
				CastExpr cast                                                                                           => ScanExpression(cast.Expression),
				MemberExpr member                                                                                       => ScanExpression(member.Object),
				DynamicMemberExpr member                                                                                => ScanExpression(member.Object) | ScanExpression(member.Name),
				DynamicVarExpr dynamicVar                                                                               => ScanExpression(dynamicVar.Name),
				ArrayIndexExpr arrayIndex                                                                               => ScanExpression(arrayIndex.Target) | ScanExpression(arrayIndex.Index),
				MultiArrayIndexExpr arrayIndex                                                                          => ScanExpression(arrayIndex.Target) | ScanExpressions(arrayIndex.Indices),
				IdentifierExpr identifier                                                                               => EventFlags.GetValueOrDefault(identifier.Name),
				CallExpr call                                                                                           => ScanExpressions(call.Args),
				ChainedCallExpr call                                                                                    => ScanExpression(call.Call) | ScanExpressions(call.Args),
				MethodCallExpr call                                                                                     => ScanExpression(call.Object) | ScanExpressions(call.Args),
				DynamicMethodCallExpr call                                                                              => ScanExpression(call.Object) | ScanExpression(call.Name) | ScanExpressions(call.Args),
				NewObjectExpr newObject                                                                                 => ScanExpressions(newObject.Args),
				NewArrayExpr newArray                                                                                   => ScanExpressions(newArray.Dimensions),
				_                                                                                                       => 0,
			};
	}

	private sealed class Emitter(BytecodeWriter bytecode, Dictionary<string, Expr> constants, ScriptGrammar grammar)
	{
		private readonly Stack<List<int>>                                                 _breakPatches            = new();
		private readonly Stack<List<int>>                                                 _continuePatches         = new();
		private readonly Stack<Dictionary<string, (List<int> Break, List<int> Continue)>> _labeledLoops            = new([new(StringComparer.Ordinal)]);
		private readonly Stack<List<int>>                                                 _conditionFailPatches    = new();
		private readonly Stack<List<int>>                                                 _conditionSuccessPatches = new();
		private readonly Stack<HashSet<string>>                                           _tempAliases             = new();

		private readonly Stack<Dictionary<Expr, int>> _cachedVariables = new();

		// Labels aren't lexically nested within a function the way loops are, so each function
		// just gets one flat dictionary - but a lambda expression triggers a nested EmitFunction
		// call while the enclosing function's emission is still in progress (see the LambdaExpr
		// case in Emit), so these are stacked per function-emission, not per-loop.
		// Seeded with one base scope so a goto/label statement written at top level (outside any
		// function) also resolves, without EmitFunction needing to special-case "am I nested".
		private readonly Stack<Dictionary<string, int>>       _labelAddresses     = new([new Dictionary<string, int>(StringComparer.Ordinal)]);
		private readonly Stack<Dictionary<string, List<int>>> _pendingGotoPatches = new([new Dictionary<string, List<int>>(StringComparer.Ordinal)]);
		private          int                                  _newObjectCount;
		private          bool                                 _dynamicMethodName;

		public void EmitFunction(FunctionNode node, bool isLambda = false)
		{
			bytecode.Emit(Opcode.Jmp);
			var jmpLoc                                               = bytecode.EmitNumberOperandPlaceholder();
			var functionName                                         = node.Public && (grammar == ScriptGrammar.GS1 || node.ObjectName != "universe") ? "public." : "";
			if (!string.IsNullOrEmpty(node.ObjectName)) functionName += node.ObjectName + ".";
			functionName += node.Name;
			if (grammar == ScriptGrammar.GS1 && node.ObjectName == "universe") functionName = (node.Public ? "public." : "") + node.Name + "," + functionName;
			var addedFunction                                                               = bytecode.AddFunction(functionName, node.Name, node.ObjectName != null, bytecode.OpIndex, 0);
			if (grammar == ScriptGrammar.GS2 && addedFunction && node.ObjectName == null && node.Name == "onServerListerConnect")
				bytecode.AddFunction("universe." + node.Name, node.Name, true, bytecode.OpIndex, 0);
			_tempAliases.Push(CollectTempAliases(node));
			_labelAddresses.Push(new Dictionary<string, int>(StringComparer.Ordinal));
			_pendingGotoPatches.Push(new Dictionary<string, List<int>>(StringComparer.Ordinal));
			_labeledLoops.Push(new(StringComparer.Ordinal));
			bytecode.Emit(Opcode.TypeArray);
			for (var i = node.Args.Count - 1; i >= 0; --i) Emit(node.Args[i]);
			bytecode.Emit(Opcode.FuncParamsEnd);
			bytecode.Emit(Opcode.FunctionStart);
			if (node.Body.Exists(ContainsCall)) bytecode.Emit(Opcode.CmdCall);
			Dictionary<string, Expr>             cacheBindings        = new(StringComparer.Ordinal);
			Dictionary<string, Expr>             initialCacheBindings = new(StringComparer.Ordinal);
			List<(Expr Expression, bool InLoop)> references           = [];
			foreach (var reference in VariableReferences(new BlockStmt(node.Body)))
			{
				var expression = reference.Expression;
				if (reference.Rebind && expression is MemberExpr rebound)
				{
					cacheBindings[rebound.Name] = rebound;
					continue;
				}

				if (grammar == ScriptGrammar.GS2)
				{
					// Bare cache references reuse the first binding seen for the name.
					if (expression is MemberExpr { Object: IdentifierExpr { Name: "temp" } } tempMember)
					{
						cacheBindings.TryAdd(tempMember.Name, tempMember);
						initialCacheBindings.TryAdd(tempMember.Name, cacheBindings[tempMember.Name]);
					}
					else if (expression is IdentifierExpr identifier)
					{
						cacheBindings.TryAdd(identifier.Name, identifier);
						initialCacheBindings.TryAdd(identifier.Name, cacheBindings[identifier.Name]);
						expression = cacheBindings[identifier.Name];
					}
				}

				references.Add((expression, reference.InLoop));
			}

			var cachedVariables =
				OrderByNativeHash(references.GroupBy(reference => reference.Expression), group => group.Key is IdentifierExpr identifier ? identifier.Name : ((IdentifierExpr)((MemberExpr)group.Key).Object).Name + "." + ((MemberExpr)group.Key).Name)
					.Where(group => (!isLambda || grammar == ScriptGrammar.GS1) && (group.Count() >= 5 || group.Any(reference => reference.InLoop)))
					.Select((group, index) => (group.Key, Index: index))
					.ToDictionary(pair => pair.Key, pair => pair.Index);
			foreach (var (reference, index) in cachedVariables)
			{
				if (reference is IdentifierExpr { Name: "params" }) bytecode.Emit(Opcode.Params);
				else
				{
					if (reference is MemberExpr member) EmitMemberOwner(member.Object);
					bytecode.Emit(Opcode.TypeVar);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(reference is IdentifierExpr identifier ? identifier.Name : ((MemberExpr)reference).Name));
					if (reference is MemberExpr) bytecode.Emit(Opcode.MemberAccess);
				}

				bytecode.Emit(Opcode.ResolveVar);
				bytecode.Emit(Opcode.StoreRegister);
				bytecode.EmitNumberOperand(index);
				bytecode.Emit(Opcode.IndexDec);
			}

			foreach (var (name, binding) in initialCacheBindings)
				if (binding is MemberExpr && cachedVariables.TryGetValue(binding, out var register))
					cachedVariables[new IdentifierExpr(name)] = register;
			_cachedVariables.Push(cachedVariables);
			foreach (var statement in node.Body) EmitStatement(statement);
			_cachedVariables.Pop();
			var unresolvedGotoPatches = _pendingGotoPatches.Pop();
			_labeledLoops.Pop();
			_labelAddresses.Pop();
			if (unresolvedGotoPatches.Count > 0) throw new InvalidOperationException($"goto to undefined label(s) in function '{functionName}': {string.Join(", ", unresolvedGotoPatches.Keys)}");
			_tempAliases.Pop();
			var lastStatement = LastMeaningfulStatement(node.Body);
			if (grammar == ScriptGrammar.GS2 ? lastStatement is not ReturnStmt : node.Name != "getPreviewSpriteCount" && (lastStatement == null || !DefinitelyReturns(lastStatement)))
			{
				bytecode.Emit(Opcode.TypeNumber);
				bytecode.EmitDynamicNumber(0);
				bytecode.Emit(Opcode.Ret);
			}

			if (!isLambda)
			{
				if (addedFunction) bytecode.AddPrejumpPatch(jmpLoc);
			}
			else bytecode.PatchShort(jmpLoc, bytecode.OpIndex);
		}

		public void EmitTopLevel(List<Stmt> statements)
		{
			foreach (var statement in statements) EmitStatement(statement);
		}

		private IEnumerable<(Expr Expression, bool InLoop, bool Rebind)> VariableReferences(AstNode node, bool inLoop = false)
		{
			if (grammar == ScriptGrammar.GS2 && node is ExprStmt unused && IsUnusedInlineExpression(unused.Expression)) yield break;
			if (grammar == ScriptGrammar.GS2 && node is WithStmt) yield break;
			if (grammar == ScriptGrammar.GS2 && node is StringCastExpr) yield break;
			if (grammar == ScriptGrammar.GS1 && IsGs1EmbeddedExpression(node)) yield break;
			if (grammar == ScriptGrammar.GS2 && IsGs2InlineExpression(node)) yield break;
			if (grammar == ScriptGrammar.GS2 && node is ForEachStmt { Name: MemberExpr { Object: IdentifierExpr { Name: "temp" } } loopName }) yield return (loopName, true, true);
			if (grammar == ScriptGrammar.GS2 && node is MethodCallExpr { Object: IdentifierExpr { Name: "this" or "thiso" } } method) yield return (new MemberExpr(method.Object, method.Name), inLoop, false);
			if (grammar == ScriptGrammar.GS2 && node is DynamicMemberExpr { Object: IdentifierExpr { Name: "this" } } dynamicMember) yield return (dynamicMember.Object, inLoop, false);
			if (grammar == ScriptGrammar.GS2 && node is DynamicMethodCallExpr { Object: IdentifierExpr { Name: "this" } } dynamicMethod) yield return (dynamicMethod.Object, inLoop, false);
			if (node is MemberExpr { Object: IdentifierExpr scope } member && (scope.Name == "this" || grammar == ScriptGrammar.GS2 && scope.Name is "temp" or "thiso"))
			{
				yield return (member, inLoop, false);
				yield break;
			}

			if (node is IdentifierExpr identifier && identifier.Name is not ("this" or "temp" or "thiso" or "playero" or "pi") && (grammar == ScriptGrammar.GS2 || identifier.Name != "params")) yield return (identifier, inLoop, false);
			if (node is CallExpr call &&
			    !BuiltInCalls.ContainsKey(call.Name) &&
			    !NonReversedBuiltInCalls.ContainsKey(call.Name) &&
			    call.Name is not ("arraylen" or "sarraylen" or "waitfor" or "sleep" or "setarray" or "makevar" or "format" or "strtofloat"))
				yield return (new IdentifierExpr(call.Name), inLoop, false);
			if (node is ForStmt { Init: { } init })
				foreach (var reference in VariableReferences(init, true))
					yield return reference;
			IEnumerable<AstNode> children = node switch
			{
				ExprStmt statement          => [statement.Expression],
				ReturnStmt statement        => [statement.Expression],
				InlineStmt statement        => [statement.Statement],
				BlockStmt block             => block.Body,
				IfStmt conditional          => [conditional.Condition, .. conditional.ThenBody, .. conditional.ElseBody],
				ForStmt loop                => [.. new Expr?[] { loop.Condition, loop.Post }.OfType<Expr>(), .. loop.Body],
				WhileStmt loop              => [loop.Condition, .. loop.Body],
				ForEachStmt loop            => [loop.Name, loop.Source, .. loop.Body],
				SwitchStmt statement        => [statement.Expression, .. statement.Cases.SelectMany(item => item.Labels.OfType<Expr>().Cast<AstNode>().Concat(item.Body))],
				WithStmt statement          => [statement.Target, .. statement.Body],
				BinaryExpr binary           => [binary.Left, binary.Right],
				UnaryExpr unary             => [unary.Expression],
				TernaryExpr ternary         => [ternary.Condition, ternary.WhenTrue, ternary.WhenFalse],
				MemberExpr value            => [value.Object],
				DynamicMemberExpr value     => [value.Object, value.Name],
				DynamicVarExpr value        => [value.Name],
				ArrayIndexExpr value        => [value.Target, value.Index],
				MultiArrayIndexExpr value   => [value.Target, .. value.Indices],
				ArrayLiteralExpr value      => value.Values,
				InExpr value                => [.. new Expr?[] { value.Expression, value.Lower, value.Upper }.OfType<Expr>()],
				StringCastExpr value        => [value.Expression],
				CastExpr value              => [value.Expression],
				CallExpr value              => value.Args,
				MethodCallExpr value        => [value.Object, .. value.Args],
				DynamicMethodCallExpr value => [value.Object, .. value.Args],
				_                           => [],
			};
			foreach (var child in children)
			foreach (var reference in VariableReferences(child, inLoop || node is ForStmt or WhileStmt or ForEachStmt))
				yield return reference;
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		private static bool IsGs1EmbeddedExpression(AstNode node) =>
			node is Expr { IsGs1Embedded: true } or DynamicVarExpr or CastExpr { Type: "float" } ||
			node is CallExpr call && (BuiltInCalls.ContainsKey(call.Name) || NonReversedBuiltInCalls.ContainsKey(call.Name) || call.Name is "arraylen" or "sarraylen" or "strtofloat" or "setarray");

		private static bool IsGs2InlineExpression(AstNode node) =>
			node switch
			{
				CastExpr { Type: "int" or "float" } => true,
				CallExpr call => BuiltInCalls.ContainsKey(call.Name) || call.Name != "pow" && NonReversedBuiltInCalls.ContainsKey(call.Name) || call.Name is "arraylen" or "sarraylen" or "waitfor" or "sleep" or "setarray" or "makevar" or "format",
				MethodCallExpr call => call.Name is "size" or "type" or "indices" or "link" or "index" or "length" or "substring" or "pos" or "charat" or "starts" or "ends" or "trim" or "tokenize" or "positions" or "subarray" or "clear" or "add"
					or "delete" or "insert" or "remove" or "replace" or "lower" or "upper",
				_ => false,
			};

		private static bool DefinitelyReturns(Stmt statement) =>
			statement switch
			{
				ReturnStmt                 => true,
				InlineStmt inlineStatement => DefinitelyReturns(inlineStatement.Statement),
				BlockStmt blockStatement   => LastMeaningfulStatement(blockStatement.Body) is { } lastStatement && DefinitelyReturns(lastStatement),
				IfStmt ifStatement         => ifStatement.HasElse && BodyDefinitelyReturns(ifStatement.ThenBody) && BodyDefinitelyReturns(ifStatement.ElseBody),
				_                          => false,
			};

		private static bool BodyDefinitelyReturns(List<Stmt> body) => LastMeaningfulStatement(body) is { } lastStatement && DefinitelyReturns(lastStatement);

		private static Stmt? LastMeaningfulStatement(List<Stmt> body)
		{
			for (var i = body.Count - 1; i >= 0; --i)
				if (body[i] is not BlockStmt { Body.Count: 0 })
					return body[i];
			return null;
		}

		private void Emit(Expr expr) => Emit(expr, false, true, 0);

		private void EmitUnusedExpression(Expr expr)
		{
			Emit(
				expr,
				false,
				true,
				0,
				false,
				false,
				false
			);
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		private static bool IsUnusedInlineExpression(Expr expr) =>
			(IsGs2InlineExpression(expr) || expr is StringCastExpr) &&
			expr switch
			{
				CallExpr call              => call.Name != "waitfor" && !NonReturningBuiltInCalls.Contains(call.Name),
				MethodCallExpr call        => !NonReturningMethodCalls.Contains(call.Name),
				CastExpr or StringCastExpr => true,
				_                          => false,
			};

		public void EmitStatement(Stmt statement) => EmitStatement(statement, true);

		private void EmitStatement(Stmt statement, bool discardCallReturn)
		{
			if (statement is ExprStmt exprStatement)
			{
				if (grammar == ScriptGrammar.GS2 && IsUnusedInlineExpression(exprStatement.Expression)) return;
				if (!discardCallReturn) Emit(exprStatement.Expression);
				else EmitUnusedExpression(exprStatement.Expression);
				if (!discardCallReturn)
				{
				}
				else if (exprStatement.Expression is CallExpr call && NonReturningBuiltInCalls.Contains(call.Name))
				{
				}
				else if (exprStatement.Expression is MethodCallExpr methodCall && NonReturningMethodCalls.Contains(methodCall.Name))
				{
				}
				else if (exprStatement.Expression is CallExpr or MethodCallExpr or DynamicMethodCallExpr) bytecode.Emit(Opcode.IndexDec);
			}
			else if (statement is ReturnStmt returnStatement)
			{
				Emit(returnStatement.Expression);
				bytecode.Emit(Opcode.Ret);
			}
			else if (statement is IfStmt ifStatement) EmitIf(ifStatement);
			else if (statement is ForStmt forStatement) EmitFor(forStatement);
			else if (statement is ForEachStmt forEachStatement) EmitForEach(forEachStatement);
			else if (statement is WhileStmt whileStatement) EmitWhile(whileStatement);
			else if (statement is WithStmt withStatement) EmitWith(withStatement);
			else if (statement is SwitchStmt switchStatement) EmitSwitch(switchStatement);
			else if (statement is BreakStmt breakStatement) EmitBreak(breakStatement.Label);
			else if (statement is ContinueStmt continueStatement) EmitContinue(continueStatement.Label);
			else if (statement is GotoStmt gotoStatement) EmitGoto(gotoStatement);
			else if (statement is LabelStmt labelStatement) EmitLabel(labelStatement);
			else if (statement is InlineStmt inlineStatement) EmitStatement(inlineStatement.Statement);
			else if (statement is BlockStmt blockStatement)
				foreach (var stmt in blockStatement.Body)
					EmitStatement(stmt);
			else if (statement is NewStmt newStatement) EmitNewStatement(newStatement);
		}

		private void EmitIf(IfStmt statement)
		{
			if (grammar == ScriptGrammar.GS2 && statement.Condition is BoolExpr && statement.ThenBody.Count == 0 && statement.ElseBody.Count == 0) return;
			if (statement.ThenBody.Count == 0 && statement.ElseBody.Count == 0)
			{
				Emit(statement.Condition, false, false, 0);
				bytecode.Emit(Opcode.IndexDec);
				return;
			}

			List<int> conditionFailPatches    = [];
			List<int> conditionSuccessPatches = [];
			_conditionFailPatches.Push(conditionFailPatches);
			_conditionSuccessPatches.Push(conditionSuccessPatches);
			if (UsesInlineForCondition(statement.Condition)) Emit(statement.Condition);
			else Emit(statement.Condition, false, false, 0, true);
			_conditionSuccessPatches.Pop();
			_conditionFailPatches.Pop();
			if (!IsBooleanExpr(statement.Condition) && (NeedsNumericConversion(statement.Condition) || grammar == ScriptGrammar.GS1 && statement.Condition is TernaryExpr)) bytecode.Emit(Opcode.ConvToFloat);
			foreach (var patch in conditionSuccessPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			bytecode.Emit(Opcode.If);
			var failLoc = bytecode.EmitNumberOperandPlaceholder();
			foreach (var stmt in statement.ThenBody) EmitStatement(stmt);
			var hasElse    = statement.HasElse && (grammar == ScriptGrammar.GS2 || statement.ElseBody.Count > 0);
			var failTarget = bytecode.OpIndex + (hasElse ? 1 : 0);
			bytecode.PatchShort(failLoc, failTarget);
			foreach (var patch in conditionFailPatches) bytecode.PatchShort(patch, failTarget);
			if (hasElse)
			{
				bytecode.Emit(Opcode.Jmp);
				var exitLoc = bytecode.EmitNumberOperandPlaceholder();
				foreach (var stmt in statement.ElseBody) EmitStatement(stmt);
				bytecode.PatchShort(exitLoc, bytecode.OpIndex);
			}
		}

		private void EmitFor(ForStmt statement)
		{
			if (statement.Init != null) EmitUnusedExpression(statement.Init);
			var       start                   = bytecode.OpIndex;
			List<int> conditionFailPatches    = [];
			List<int> conditionSuccessPatches = [];
			_conditionFailPatches.Push(conditionFailPatches);
			_conditionSuccessPatches.Push(conditionSuccessPatches);
			if (grammar == ScriptGrammar.GS1) Emit(statement.Condition, statement.Condition is BinaryExpr { Op: "=" }, true, 0);
			else if (UsesInlineForCondition(statement.Condition)) Emit(statement.Condition);
			else Emit(statement.Condition, statement.Condition is BinaryExpr { Op: "=" }, false, 0, true);
			_conditionSuccessPatches.Pop();
			_conditionFailPatches.Pop();
			if (!IsBooleanExpr(statement.Condition) && (NeedsNumericConversion(statement.Condition) || grammar == ScriptGrammar.GS2 && statement.Condition is BinaryExpr { Op: "=" })) bytecode.Emit(Opcode.ConvToFloat);
			foreach (var patch in conditionSuccessPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			bytecode.Emit(Opcode.If);
			var       breakLoc     = bytecode.EmitNumberOperandPlaceholder();
			List<int> breakPatches = [];
			_breakPatches.Push(breakPatches);
			List<int> continuePatches = [];
			_continuePatches.Push(continuePatches);
			if (statement.Label != null) _labeledLoops.Peek().Add(statement.Label, (breakPatches, []));
			bytecode.Emit(Opcode.CmdCall);
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			foreach (var patch in continuePatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			if (statement.Label != null)
				foreach (var patch in _labeledLoops.Peek()[statement.Label].Continue)
					bytecode.PatchShort(patch, statement.Post == null ? start : bytecode.OpIndex);
			if (statement.Post != null) EmitUnusedExpression(statement.Post);
			bytecode.Emit(Opcode.Jmp);
			bytecode.EmitDynamicNumber(start);
			_continuePatches.Pop();
			_breakPatches.Pop();
			if (statement.Label != null) _labeledLoops.Peek().Remove(statement.Label);
			bytecode.PatchShort(breakLoc, bytecode.OpIndex);
			foreach (var patch in breakPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			foreach (var patch in conditionFailPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
		}

		private void EmitForEach(ForEachStmt statement)
		{
			Emit(statement.Name);
			if (grammar == ScriptGrammar.GS2 && statement.Name is MemberExpr { Object: IdentifierExpr { Name: "temp" } } loopName && _cachedVariables.TryPeek(out var cache) && cache.TryGetValue(loopName, out var register))
				cache[new IdentifierExpr(loopName.Name)] = register;
			Emit(statement.Source);
			bytecode.Emit(Opcode.ConvToObject);
			bytecode.Emit(Opcode.TypeNumber);
			bytecode.EmitDynamicNumber(0);
			var start = bytecode.OpIndex;
			bytecode.Emit(Opcode.Foreach);
			var       breakLoc     = bytecode.EmitNumberOperandPlaceholder();
			List<int> breakPatches = [];
			_breakPatches.Push(breakPatches);
			List<int> continuePatches = [];
			_continuePatches.Push(continuePatches);
			if (statement.Label != null) _labeledLoops.Peek().Add(statement.Label, (breakPatches, continuePatches));
			bytecode.Emit(Opcode.CmdCall);
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			foreach (var patch in continuePatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			bytecode.Emit(Opcode.Inc);
			bytecode.Emit(Opcode.Jmp);
			bytecode.EmitDynamicNumber(start);
			_continuePatches.Pop();
			_breakPatches.Pop();
			if (statement.Label != null) _labeledLoops.Peek().Remove(statement.Label);
			bytecode.PatchShort(breakLoc, bytecode.OpIndex);
			foreach (var patch in breakPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			bytecode.Emit(Opcode.IndexDec);
			if (grammar == ScriptGrammar.GS2)
			{
				bytecode.Emit(Opcode.IndexDec);
				bytecode.Emit(Opcode.IndexDec);
			}
		}

		private void EmitWhile(WhileStmt statement)
		{
			var       start                   = bytecode.OpIndex;
			List<int> conditionFailPatches    = [];
			List<int> conditionSuccessPatches = [];
			_conditionFailPatches.Push(conditionFailPatches);
			_conditionSuccessPatches.Push(conditionSuccessPatches);
			Emit(statement.Condition, false, false, 0, true);
			_conditionSuccessPatches.Pop();
			_conditionFailPatches.Pop();
			if (!IsBooleanExpr(statement.Condition) && NeedsNumericConversion(statement.Condition)) bytecode.Emit(Opcode.ConvToFloat);
			foreach (var patch in conditionSuccessPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			List<int> breakPatches = [];
			_breakPatches.Push(breakPatches);
			List<int> continuePatches = [];
			_continuePatches.Push(continuePatches);
			if (statement.Label != null) _labeledLoops.Peek().Add(statement.Label, (breakPatches, []));
			bytecode.Emit(Opcode.If);
			var breakLoc = bytecode.EmitNumberOperandPlaceholder();
			breakPatches.Add(breakLoc);
			bytecode.Emit(Opcode.CmdCall);
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			foreach (var patch in continuePatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			if (statement.Label != null)
				foreach (var patch in _labeledLoops.Peek()[statement.Label].Continue)
					bytecode.PatchShort(patch, start);
			bytecode.Emit(Opcode.Jmp);
			bytecode.EmitNumberOperand(start);
			_continuePatches.Pop();
			_breakPatches.Pop();
			if (statement.Label != null) _labeledLoops.Peek().Remove(statement.Label);
			foreach (var patch in breakPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			foreach (var patch in conditionFailPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
		}

		private void EmitWith(WithStmt statement)
		{
			if (grammar == ScriptGrammar.GS2 && _cachedVariables.Count > 0)
			{
				var cache = _cachedVariables.Pop();
				try
				{
					EmitWith(statement);
				}
				finally
				{
					_cachedVariables.Push(cache);
				}

				return;
			}

			Emit(statement.Target);
			bytecode.Emit(Opcode.ConvToObject);
			bytecode.Emit(Opcode.With);
			var exitLoc = bytecode.EmitNumberOperandPlaceholder();
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			bytecode.Emit(Opcode.WithEnd);
			bytecode.PatchShort(exitLoc, bytecode.OpIndex);
		}

		private void EmitSwitch(SwitchStmt statement)
		{
			bytecode.Emit(Opcode.Jmp);
			var       caseTestLoc  = bytecode.EmitNumberOperandPlaceholder();
			List<int> caseStarts   = [];
			List<int> breakPatches = [];
			_breakPatches.Push(breakPatches);
			foreach (var switchCase in statement.Cases)
			{
				var start = bytecode.OpIndex;
				foreach (var _ in switchCase.Labels) caseStarts.Add(start);
				List<int> continuePatches = [];
				if (grammar == ScriptGrammar.GS1) _continuePatches.Push(continuePatches);
				foreach (var stmt in switchCase.Body) EmitStatement(stmt);
				if (grammar == ScriptGrammar.GS1) _continuePatches.Pop();
				foreach (var patch in continuePatches) bytecode.PatchShort(patch, start);
			}

			_breakPatches.Pop();
			if (grammar == ScriptGrammar.GS2)
			{
				bytecode.Emit(Opcode.Jmp);
				breakPatches.Add(bytecode.EmitNumberOperandPlaceholder());
			}

			bytecode.PatchShort(caseTestLoc, bytecode.OpIndex);
			Emit(statement.Expression);
			var caseIndex = 0;
			foreach (var switchCase in statement.Cases)
			foreach (var label in switchCase.Labels)
			{
				if (label != null)
				{
					bytecode.Emit(Opcode.CopyLastOp);
					Emit(label);
					bytecode.Emit(Opcode.Eq);
					bytecode.Emit(Opcode.SetIndexTrue);
				}
				else bytecode.Emit(Opcode.Jmp);

				bytecode.EmitDynamicNumber(caseStarts[caseIndex++]);
			}

			foreach (var patch in breakPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			bytecode.Emit(Opcode.IndexDec);
		}

		private void EmitBreak(string? label)
		{
			if (label != null)
			{
				if (!_labeledLoops.Peek().TryGetValue(label, out var loop)) throw new InvalidOperationException($"break to undefined loop label '{label}'");
				bytecode.Emit(Opcode.Jmp);
				loop.Break.Add(bytecode.EmitNumberOperandPlaceholder());
				return;
			}

			if (_breakPatches.Count == 0) return;
			bytecode.Emit(Opcode.Jmp);
			_breakPatches.Peek().Add(bytecode.EmitNumberOperandPlaceholder());
		}

		private void EmitContinue(string? label)
		{
			if (label != null)
			{
				if (!_labeledLoops.Peek().TryGetValue(label, out var loop)) throw new InvalidOperationException($"continue to undefined loop label '{label}'");
				bytecode.Emit(Opcode.Jmp);
				loop.Continue.Add(bytecode.EmitNumberOperandPlaceholder());
				return;
			}

			if (_continuePatches.Count == 0) return;
			bytecode.Emit(Opcode.Jmp);
			_continuePatches.Peek().Add(bytecode.EmitNumberOperandPlaceholder());
		}

		private void EmitGoto(GotoStmt statement)
		{
			bytecode.Emit(Opcode.Jmp);
			if (_labelAddresses.Peek().TryGetValue(statement.Label, out var address))
			{
				bytecode.EmitDynamicNumber(address);
				return;
			}

			var patches                                                                          = _pendingGotoPatches.Peek();
			if (!patches.TryGetValue(statement.Label, out var pending)) patches[statement.Label] = pending = [];
			pending.Add(bytecode.EmitNumberOperandPlaceholder());
		}

		private void EmitLabel(LabelStmt statement)
		{
			_labelAddresses.Peek()[statement.Label] = bytecode.OpIndex;
			var patches = _pendingGotoPatches.Peek();
			if (!patches.Remove(statement.Label, out var pending)) return;
			foreach (var patch in pending) bytecode.PatchShort(patch, bytecode.OpIndex);
		}

		private void EmitNewStatement(NewStmt statement)
		{
			if (grammar == ScriptGrammar.GS2 && _cachedVariables.TryPop(out var cache))
			{
				try
				{
					EmitNewStatement(statement);
				}
				finally
				{
					_cachedVariables.Push(cache);
				}

				return;
			}

			foreach (var arg in statement.Args) Emit(arg);
			if (grammar == ScriptGrammar.GS2 && statement.Args.Count == 0)
			{
				bytecode.Emit(Opcode.TypeVar);
				bytecode.EmitDynamicStringIndex(bytecode.GetString("unknown_object"));
			}
			else bytecode.Emit(Opcode.InlineNew);

			bytecode.Emit(Opcode.CopyLastOp);
			bytecode.Emit(Opcode.CopyLastOp);
			bytecode.Emit(Opcode.CopyLastOp);
			bytecode.Emit(Opcode.TypeString);
			bytecode.EmitDynamicStringIndex(bytecode.GetString(statement.TypeName));
			bytecode.Emit(Opcode.ConvToString);
			bytecode.Emit(Opcode.NewObject);
			bytecode.Emit(Opcode.Assign);
			bytecode.Emit(Opcode.ConvToObject);
			bytecode.Emit(Opcode.With);
			var withLoc       = bytecode.EmitNumberOperandPlaceholder();
			var previousCount = _newObjectCount++;
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			bytecode.Emit(Opcode.WithEnd);
			bytecode.PatchShort(withLoc, bytecode.OpIndex);
			for (var i = 0; i < _newObjectCount - previousCount; ++i)
			{
				bytecode.Emit(Opcode.TypeArray);
				bytecode.Emit(Opcode.SwapLastOps);
				bytecode.Emit(Opcode.TypeVar);
				bytecode.EmitDynamicStringIndex(bytecode.GetString("addcontrol"));
				bytecode.Emit(Opcode.Call);
				bytecode.Emit(Opcode.IndexDec);
			}

			_newObjectCount--;
		}

		private void Emit(
			Expr expr,
			bool copyAssignmentTarget,
			bool logicalInline,
			int suppressedLogicalPatchOffset,
			bool controlLogical = false,
			bool negatedLogical = false,
			bool preservePostfixValue = true,
			bool leftNegatedControlAnd = false
		)
		{
			// Native inline expressions are compiled outside the function variable cache.
			if (_cachedVariables.Count > 0 && (grammar == ScriptGrammar.GS1 ? IsGs1EmbeddedExpression(expr) : IsGs2InlineExpression(expr)))
			{
				var cache = _cachedVariables.Pop();
				try
				{
					Emit(
						expr,
						copyAssignmentTarget,
						logicalInline,
						suppressedLogicalPatchOffset,
						controlLogical,
						negatedLogical,
						preservePostfixValue,
						leftNegatedControlAnd
					);
				}
				finally
				{
					_cachedVariables.Push(cache);
				}

				return;
			}

			if (_cachedVariables.TryPeek(out var variables) && variables.TryGetValue(expr, out var cachedRegister))
			{
				bytecode.Emit(Opcode.LoadRegister);
				bytecode.EmitNumberOperand(cachedRegister);
				return;
			}

			switch (expr)
			{
				case BinaryExpr { Op: "=", Left: MultiArrayIndexExpr left, Right: var right }:
					EmitMultiArrayIndex(left, true);
					if (copyAssignmentTarget) bytecode.Emit(Opcode.CopyLastOp);
					if (right is BinaryExpr { Op: "=" }) Emit(right, true, true, 0);
					else Emit(right);
					bytecode.Emit(Opcode.ArrayMultiDimAssign);
					break;
				case BinaryExpr { Op: "=", Left: ArrayIndexExpr left, Right: var right }:
					EmitArrayIndex(left, true);
					if (copyAssignmentTarget && grammar != ScriptGrammar.GS1) bytecode.Emit(Opcode.CopyLastOp);
					if (right is BinaryExpr { Op: "=" }) Emit(right, true, true, 0);
					else Emit(right);
					bytecode.Emit(Opcode.ArrayAssign);
					if (copyAssignmentTarget && grammar == ScriptGrammar.GS1) EmitArrayIndex(left, false);
					break;
				case BinaryExpr { Op: "=", Left: var left, Right: var right }:
					Emit(left);
					if (copyAssignmentTarget) bytecode.Emit(Opcode.CopyLastOp);
					if (right is BinaryExpr { Op: "=" }) Emit(right, true, true, 0);
					else Emit(right);
					bytecode.Emit(Opcode.Assign);
					break;
				case BinaryExpr { Op: var op, Left: MultiArrayIndexExpr left, Right: var right } when IsCompoundAssign(op):
					EmitMultiArrayIndex(left, true);
					EmitMultiArrayIndex(left, false);
					bytecode.Emit(op == "@=" ? Opcode.ConvToString : Opcode.ConvToFloat);
					Emit(right);
					if (op == "@=" && NeedsStringConversion(right)) bytecode.Emit(Opcode.ConvToString);
					else if (op != "@=" && NeedsNumericConversion(right)) bytecode.Emit(Opcode.ConvToFloat);
					bytecode.Emit(CompoundOpcode(op));
					bytecode.Emit(Opcode.ArrayMultiDimAssign);
					if (grammar == ScriptGrammar.GS2 && preservePostfixValue) EmitMultiArrayIndex(left, false);
					break;
				case BinaryExpr { Op: var op, Left: ArrayIndexExpr left, Right: var right } when IsCompoundAssign(op):
					EmitArrayIndex(left, true);
					EmitArrayIndex(left, false);
					bytecode.Emit(op == "@=" ? Opcode.ConvToString : Opcode.ConvToFloat);
					Emit(right);
					if (op == "@=" && NeedsStringConversion(right)) bytecode.Emit(Opcode.ConvToString);
					else if (op != "@=" && NeedsNumericConversion(right)) bytecode.Emit(Opcode.ConvToFloat);
					bytecode.Emit(CompoundOpcode(op));
					bytecode.Emit(Opcode.ArrayAssign);
					if (grammar == ScriptGrammar.GS2 && preservePostfixValue) EmitArrayIndex(left, false);
					break;
				case BinaryExpr { Op: var op, Left: var left, Right: var right } when IsCompoundAssign(op):
					Emit(left);
					if (grammar == ScriptGrammar.GS2 && preservePostfixValue) bytecode.Emit(Opcode.CopyLastOp);
					bytecode.Emit(Opcode.CopyLastOp);
					bytecode.Emit(op == "@=" ? Opcode.ConvToString : Opcode.ConvToFloat);
					Emit(right);
					if (op == "@=" && NeedsStringConversion(right)) bytecode.Emit(Opcode.ConvToString);
					else if (op != "@=" && NeedsNumericConversion(right)) bytecode.Emit(Opcode.ConvToFloat);
					bytecode.Emit(CompoundOpcode(op));
					bytecode.Emit(Opcode.Assign);
					break;
				case BinaryExpr { Op: "&&" or "||" } logical:
					if (!controlLogical && !leftNegatedControlAnd && HasNestedLogical(logical, logical.Op))
					{
						EmitLogicalChain(logical, logicalInline, suppressedLogicalPatchOffset);
						break;
					}

					List<int>? localSuccessPatches = null;
					List<int>? localFailPatches    = null;
					var        localSuccessStart   = 0;
					var        localFailStart      = 0;
					if (logical.Op == "&&" && controlLogical && _conditionSuccessPatches.Count > 0)
					{
						localSuccessPatches = _conditionSuccessPatches.Peek();
						localSuccessStart   = localSuccessPatches.Count;
					}

					if (logical.Op == "||" && controlLogical && _conditionFailPatches.Count > 0)
					{
						localFailPatches = _conditionFailPatches.Peek();
						localFailStart   = localFailPatches.Count;
					}

					var passNegatedControlAnd = grammar != ScriptGrammar.GS1 && controlLogical && logical.Left is UnaryExpr { Op: "!", Expression: BinaryExpr { Op: "&&" } };
					var leftPatchOffset = logical.Left is BinaryExpr { Op: "&&" or "||" }
						? controlLogical && logical.Op == "||" ? 6 : logical.Op == "&&" && logical.Left is BinaryExpr { Op: "||" } || negatedLogical || grammar == ScriptGrammar.GS2 ? 0 : 1
						: 0;
					var valueOnLeft    = logical.Op == "||" && (logical.Left is BinaryExpr { Op: "&&" } || grammar == ScriptGrammar.GS1 && logical.Left is BinaryExpr { Op: "||" });
					var leftAssignment = grammar == ScriptGrammar.GS1 && logical.Left is BinaryExpr { Op: "=" };
					Emit(
						logical.Left,
						leftAssignment,
						false,
						valueOnLeft ? 0 : leftPatchOffset,
						controlLogical && !valueOnLeft,
						negatedLogical,
						true,
						passNegatedControlAnd
					);
					if (leftAssignment || !IsBooleanExpr(logical.Left) && NeedsNumericConversion(logical.Left)) bytecode.Emit(Opcode.ConvToFloat);
					if (localSuccessPatches != null)
					{
						for (var i = localSuccessStart; i < localSuccessPatches.Count; ++i) bytecode.PatchShort(localSuccessPatches[i], bytecode.OpIndex);
						localSuccessPatches.RemoveRange(localSuccessStart, localSuccessPatches.Count - localSuccessStart);
					}

					if (localFailPatches != null)
					{
						for (var i = localFailStart; i < localFailPatches.Count; ++i) bytecode.PatchShort(localFailPatches[i], bytecode.OpIndex + 1);
						localFailPatches.RemoveRange(localFailStart, localFailPatches.Count - localFailStart);
					}

					bytecode.Emit(logical.Op == "&&" && controlLogical ? Opcode.If : logical.Op == "&&" ? Opcode.And : Opcode.Or);
					var loc             = bytecode.EmitNumberOperandPlaceholder();
					var rightAssignment = grammar == ScriptGrammar.GS1 && logical.Right is BinaryExpr { Op: "=" };
					Emit(
						logical.Right,
						rightAssignment,
						false,
						0,
						controlLogical,
						negatedLogical
					);
					if (rightAssignment || !IsBooleanExpr(logical.Right) && NeedsNumericConversion(logical.Right)) bytecode.Emit(Opcode.ConvToFloat);
					if (logicalInline) bytecode.Emit(Opcode.InlineConditional);
					if (logical.Op == "&&" && controlLogical && _conditionFailPatches.Count > 0) _conditionFailPatches.Peek().Add(loc);
					else if (logical.Op == "||" && controlLogical && _conditionSuccessPatches.Count > 0) _conditionSuccessPatches.Peek().Add(loc);
					else bytecode.PatchShort(loc, bytecode.OpIndex - (logicalInline ? 1 : 0) + (!logicalInline ? suppressedLogicalPatchOffset : 0));
					break;
				case TernaryExpr ternary:
					EmitTernary(ternary);
					break;
				case BinaryExpr { Op: "@", Left: var left, Right: var right } when _dynamicMethodName:
					Emit(left);
					if (grammar == ScriptGrammar.GS1 && left is StringExpr || NeedsStringConversion(left)) bytecode.Emit(Opcode.ConvToString);
					Emit(right);
					if (right is not StringExpr) bytecode.Emit(Opcode.ConvToString);
					bytecode.Emit(Opcode.Join);
					break;
				case BinaryExpr { Op: " " or "\n" or "\t", Left: var left, Right: var right } spacedConcat:
					if (grammar == ScriptGrammar.GS2 && left is StringExpr prefix) Emit(new StringExpr(prefix.Value + spacedConcat.Op));
					else
					{
						Emit(left);
						if (NeedsStringConversion(left)) bytecode.Emit(Opcode.ConvToString);
						bytecode.Emit(Opcode.TypeString);
						bytecode.EmitDynamicStringIndex(bytecode.GetString(spacedConcat.Op));
						bytecode.Emit(Opcode.Join);
					}

					Emit(right);
					if (NeedsStringConversion(right)) bytecode.Emit(Opcode.ConvToString);
					bytecode.Emit(Opcode.Join);
					break;
				case InExpr inExpr:
					Emit(inExpr.Expression);
					Emit(inExpr.Lower);
					if (inExpr.Upper != null)
					{
						if (!IsNumberExpr(inExpr.Lower)) bytecode.Emit(Opcode.ConvToFloat);
						Emit(inExpr.Upper);
						if (!IsNumberExpr(inExpr.Upper)) bytecode.Emit(Opcode.ConvToFloat);
						bytecode.Emit(Opcode.InRange);
						bytecode.EmitNumberOperand((inExpr.LowerExclusive ? 2 : 0) | (inExpr.UpperExclusive ? 1 : 0));
					}
					else
					{
						bytecode.Emit(Opcode.ConvToObject);
						bytecode.Emit(Opcode.InObj);
					}

					break;
				case BinaryExpr { Op: "==" or "!=", Left: StringCastExpr { Expression: MemberExpr left }, Right: StringCastExpr { Expression: MemberExpr right } } castCompare when grammar == ScriptGrammar.GS1:
					Emit(left);
					Emit(right);
					bytecode.Emit(Opcode.ConvToString);
					bytecode.Emit(BinaryOpcode(castCompare.Op));
					bytecode.Emit(Opcode.ConvToString);
					break;
				case BinaryExpr { Op: "==" or "!=", Left: StringCastExpr { Expression: MemberExpr left }, Right: StringCastExpr { Expression: ArrayLiteralExpr right } } arrayCastCompare when grammar == ScriptGrammar.GS1:
					Emit(left);
					Emit(new StringCastExpr(right));
					bytecode.Emit(BinaryOpcode(arrayCastCompare.Op));
					bytecode.Emit(Opcode.ConvToString);
					break;
				case BinaryExpr { Op: "==" or "!=", Left: StringCastExpr { Expression: ArrayLiteralExpr left }, Right: StringCastExpr { Expression: ArrayLiteralExpr right } } arrayPairCastCompare when grammar == ScriptGrammar.GS1:
					Emit(left);
					Emit(new StringCastExpr(right));
					bytecode.Emit(BinaryOpcode(arrayPairCastCompare.Op));
					bytecode.Emit(Opcode.ConvToString);
					break;
				case BinaryExpr binary:
					Emit(binary.Left);
					if (binary.Op == "@" && NeedsStringConversion(binary.Left)) bytecode.Emit(Opcode.ConvToString);
					if ((IsNumericOp(binary.Op) || IsComparisonOp(binary.Op)) && NeedsNumericConversion(binary.Left)) bytecode.Emit(Opcode.ConvToFloat);
					if (IsComparisonOp(binary.Op) && binary.Left is UnaryExpr { Op: "++" or "--", Postfix: true }) bytecode.Emit(Opcode.ConvToFloat);
					if (binary.Op == "%" && binary.Left is BinaryExpr { Op: "+=" or "-=" or "*=" or "/=" or "%=" }) bytecode.Emit(Opcode.ConvToFloat);
					if ((grammar == ScriptGrammar.GS2 ? binary.Op is "&" or "|" or "xor" or "<<" or ">>" : binary.Op == "&") && NeedsNumericConversion(binary.Left)) bytecode.Emit(Opcode.ConvToFloat);
					if (grammar == ScriptGrammar.GS1 && binary.Op == "@" && binary.Left is StringCastExpr && binary.Right is TernaryExpr) bytecode.Emit(Opcode.ConvToString);
					if (grammar == ScriptGrammar.GS1 && IsNumericOp(binary.Op) && UsesPreviewSpriteTernary(binary.Left)) bytecode.Emit(Opcode.ConvToFloat);
					Emit(binary.Right, binary.Right is BinaryExpr { Op: "=" }, true, 0);
					if ((IsNumericOp(binary.Op) || IsComparisonOp(binary.Op)) && NeedsNumericConversion(binary.Right)) bytecode.Emit(Opcode.ConvToFloat);
					if (grammar == ScriptGrammar.GS2 && binary.Op is "&" or "|" or "xor" or "<<" or ">>" && NeedsNumericConversion(binary.Right)) bytecode.Emit(Opcode.ConvToFloat);
					if (grammar == ScriptGrammar.GS1 && IsNumericOp(binary.Op) && UsesSignTernary(binary.Right)) bytecode.Emit(Opcode.ConvToFloat);
					if (binary.Op == "@" && (_dynamicMethodName || NeedsStringConversion(binary.Right))) bytecode.Emit(Opcode.ConvToString);
					bytecode.Emit(BinaryOpcode(binary.Op));
					break;
				case MemberExpr member:
					EmitMemberOwner(member.Object);
					if (NeedsObjectConversion(member.Object) || member.Object is not IdentifierExpr { Name: "this" } && variables?.ContainsKey(member.Object) == true || member.Object is ArrayIndexExpr) bytecode.Emit(Opcode.ConvToObject);
					bytecode.Emit(Opcode.TypeVar);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(member.Name));
					bytecode.Emit(Opcode.MemberAccess);
					break;
				case DynamicMemberExpr member:
					EmitMemberOwner(member.Object, true);
					if (NeedsObjectConversion(member.Object) || grammar == ScriptGrammar.GS2 && member.Object is ArrayIndexExpr or IdentifierExpr { Name: "this" }) bytecode.Emit(Opcode.ConvToObject);
					if (grammar == ScriptGrammar.GS1 && member.Name is StringCastExpr { Expression: IdentifierExpr or MemberExpr { Object: IdentifierExpr { Name: "temp" } } } nameCast)
					{
						Emit(nameCast.Expression);
						if (NeedsNumericConversion(nameCast.Expression)) bytecode.Emit(Opcode.ConvToFloat);
						bytecode.Emit(Opcode.Add);
					}
					else
					{
						EmitDynamicMemberName(member.Name);
						if (grammar == ScriptGrammar.GS1 && NeedsStringConversion(member.Name)) bytecode.Emit(Opcode.ConvToString);
					}

					bytecode.Emit(Opcode.MemberAccess);
					break;
				case StringCastExpr { Expression: IdentifierExpr id } when grammar == ScriptGrammar.GS1 && _tempAliases.TryPeek(out var tempAliases) && tempAliases.Contains(id.Name):
					Emit(new MemberExpr(new IdentifierExpr("temp"), id.Name));
					bytecode.Emit(Opcode.ConvToString);
					break;
				case StringCastExpr cast:
					Emit(cast.Expression);
					if (grammar == ScriptGrammar.GS1 || NeedsStringConversion(cast.Expression)) bytecode.Emit(Opcode.ConvToString);
					if (grammar == ScriptGrammar.GS1 && ExpressionTypeOf(cast.Expression) == ExprType.Array) bytecode.Emit(Opcode.MemberAccess);
					break;
				case ArrayIndexExpr index:
					EmitArrayIndex(index, false);
					break;
				case MultiArrayIndexExpr index:
					EmitMultiArrayIndex(index, false);
					break;
				case DynamicVarExpr dynamicVar:
					if (grammar == ScriptGrammar.GS1)
					{
						Emit(dynamicVar.Name);
						if (NeedsStringConversion(dynamicVar.Name)) bytecode.Emit(Opcode.ConvToString);
						bytecode.Emit(Opcode.ObjFromString);
					}
					else Emit(new CallExpr("makevar", [dynamicVar.Name]));

					break;
				case IdentifierExpr id when constants.TryGetValue(id.Name, out var constant):
					Emit(constant);
					break;
				case IdentifierExpr { Name: "this" } when grammar == ScriptGrammar.GS1:
					bytecode.Emit(Opcode.This);
					break;
				case IdentifierExpr { Name: "temp" } when grammar == ScriptGrammar.GS1:
					bytecode.Emit(Opcode.Temp);
					break;
				case IdentifierExpr { Name: "thiso" } when grammar == ScriptGrammar.GS1:
					bytecode.Emit(Opcode.Thiso);
					break;
				case IdentifierExpr { Name: "player" }:
					bytecode.Emit(Opcode.Player);
					break;
				case IdentifierExpr { Name: "playero" } when grammar == ScriptGrammar.GS1:
					bytecode.Emit(Opcode.Playero);
					break;
				case IdentifierExpr { Name: "level" }:
					bytecode.Emit(Opcode.Level);
					break;
				case IdentifierExpr { Name: "pi" }:
					bytecode.Emit(Opcode.Pi);
					break;
				case IdentifierExpr { Name: "params" }:
					bytecode.Emit(Opcode.Params);
					break;
				case IdentifierExpr id:
					bytecode.Emit(Opcode.TypeVar);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(id.Name));
					break;
				case EnumExpr enumExpr:
					// Match the official compiler: Enum::Member is emitted as a runtime
					// variable reference, never constant-folded to its numeric value.
					bytecode.Emit(Opcode.TypeVar);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(enumExpr.EnumName + "::" + enumExpr.MemberName));
					break;
				case NumberExpr number:
					EmitNativeNumber(double.Parse(number.Text, CultureInfo.InvariantCulture));
					break;
				case StringExpr str:
					bytecode.Emit(Opcode.TypeString);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(str.Value));
					break;
				case BoolExpr boolean:
					bytecode.Emit(boolean.Value ? Opcode.TypeTrue : Opcode.TypeFalse);
					break;
				case NullExpr:
					bytecode.Emit(Opcode.TypeNull);
					break;
				case UnaryExpr { Op: "-", Expression: NumberExpr number }:
					EmitNativeNumber(-double.Parse(number.Text, CultureInfo.InvariantCulture));
					break;
				case UnaryExpr { Op: "-", Expression: var unaryValue }:
					Emit(unaryValue);
					if (NeedsNumericConversion(unaryValue) || grammar == ScriptGrammar.GS1 && unaryValue is CastExpr or TernaryExpr) bytecode.Emit(Opcode.ConvToFloat);
					bytecode.Emit(Opcode.UnarySub);
					break;
				case UnaryExpr { Op: "!", Expression: var notValue }:
					Emit(
						notValue,
						grammar == ScriptGrammar.GS1 && notValue is BinaryExpr { Op: "=" },
						false,
						0,
						leftNegatedControlAnd,
						true
					);
					if (!IsBooleanExpr(notValue)) bytecode.Emit(Opcode.ConvToFloat);
					bytecode.Emit(Opcode.Not);
					break;
				case UnaryExpr { Op: "~", Expression: var invertValue }:
					Emit(invertValue);
					if (!IsNumberExpr(invertValue)) bytecode.Emit(Opcode.ConvToFloat);
					bytecode.Emit(Opcode.BitInvert);
					break;
				case UnaryExpr { Op: "++", Expression: var incValue, Postfix: true }:
					Emit(incValue);
					if (preservePostfixValue)
					{
						bytecode.Emit(Opcode.CopyLastOp);
						bytecode.Emit(Opcode.ConvToFloat);
						bytecode.Emit(Opcode.SwapLastOps);
					}

					bytecode.Emit(Opcode.Inc);
					bytecode.Emit(Opcode.IndexDec);
					break;
				case UnaryExpr { Op: "--", Expression: var decValue, Postfix: true }:
					Emit(decValue);
					if (preservePostfixValue)
					{
						bytecode.Emit(Opcode.CopyLastOp);
						bytecode.Emit(Opcode.ConvToFloat);
						bytecode.Emit(Opcode.SwapLastOps);
					}

					bytecode.Emit(Opcode.Dec);
					bytecode.Emit(Opcode.IndexDec);
					break;
				case UnaryExpr { Op: "++", Expression: var incValue }:
					Emit(incValue);
					bytecode.Emit(Opcode.Inc);
					if (!preservePostfixValue) bytecode.Emit(Opcode.IndexDec);
					break;
				case UnaryExpr { Op: "--", Expression: var decValue }:
					Emit(decValue);
					bytecode.Emit(Opcode.Dec);
					if (!preservePostfixValue) bytecode.Emit(Opcode.IndexDec);
					break;
				case CastExpr { Type: "int", Expression: var castValue }:
					Emit(castValue);
					if (!IsNumberExpr(castValue)) bytecode.Emit(Opcode.ConvToFloat);
					bytecode.Emit(Opcode.Int);
					break;
				case CastExpr { Type: "float", Expression: var castValue }:
					Emit(castValue);
					if (!IsNumberExpr(castValue)) bytecode.Emit(Opcode.ConvToFloat);
					break;
				case CastExpr { Type: "_", Expression: var castValue }:
					Emit(castValue);
					if (castValue is not StringExpr) bytecode.Emit(Opcode.ConvToString);
					bytecode.Emit(Opcode.Translate);
					break;
				case CallExpr { Name: "strtofloat", Args: [var stringValue] } when grammar == ScriptGrammar.GS1:
					Emit(stringValue);
					bytecode.Emit(Opcode.ConvToFloat);
					break;
				case CallExpr { Name: "random" or "min" or "max" or "log" } call:
					foreach (var argument in call.Args)
					{
						Emit(argument);
						if (!IsNumberExpr(argument) || grammar == ScriptGrammar.GS1 && argument is CastExpr { Type: "float" }) bytecode.Emit(Opcode.ConvToFloat);
					}

					bytecode.Emit(BuiltInCalls[call.Name]);
					break;
				case CallExpr call when BuiltInCalls.TryGetValue(call.Name, out var builtIn):
					foreach (var argument in grammar == ScriptGrammar.GS2 ? call.Args : call.Args.AsEnumerable().Reverse())
					{
						Emit(argument);
						if (!IsNumberExpr(argument) || grammar == ScriptGrammar.GS1 && argument is CastExpr { Type: "float" }) bytecode.Emit(Opcode.ConvToFloat);
					}

					bytecode.Emit(builtIn);
					break;
				case CallExpr call when (grammar == ScriptGrammar.GS1 || call.Name != "pow") && NonReversedBuiltInCalls.TryGetValue(call.Name, out var builtIn):
					for (var i = 0; i < call.Args.Count; ++i)
					{
						Emit(call.Args[i]);
						if (!IsNumberExpr(call.Args[i]) || grammar == ScriptGrammar.GS1 && call.Args[i] is CastExpr { Type: "float" }) bytecode.Emit(Opcode.ConvToFloat);
					}

					bytecode.Emit(builtIn);
					break;
				case CallExpr { Name: "sarraylen" } call when grammar == ScriptGrammar.GS2:
					foreach (var argument in call.Args)
					{
						Emit(argument);
						bytecode.Emit(Opcode.ObjFromString);
						bytecode.Emit(Opcode.ConvToObject);
					}

					bytecode.Emit(Opcode.ObjSize);
					break;
				case CallExpr { Name: "arraylen" or "sarraylen" } call:
					for (var i = call.Args.Count - 1; i >= 0; --i)
					{
						Emit(call.Args[i]);
						if (NeedsObjectConversion(call.Args[i])) bytecode.Emit(Opcode.ConvToObject);
					}

					bytecode.Emit(Opcode.ObjSize);
					break;
				case CallExpr { Name: "waitfor" } call:
					for (var i = 0; i < call.Args.Count; ++i)
					{
						Emit(call.Args[i]);
						if (i < 2 && NeedsStringConversion(call.Args[i])) bytecode.Emit(Opcode.ConvToString);
						else if (i == 2 && !IsNumberExpr(call.Args[i])) bytecode.Emit(Opcode.ConvToFloat);
					}

					bytecode.Emit(Opcode.WaitFor);
					break;
				case CallExpr { Name: "sleep" } call:
					for (var i = 0; i < call.Args.Count; ++i)
					{
						Emit(call.Args[i]);
						if (!IsNumberExpr(call.Args[i])) bytecode.Emit(Opcode.ConvToFloat);
					}

					bytecode.Emit(Opcode.Sleep);
					break;
				case CallExpr { Name: "setarray" } call:
					if (call.Args.Count > 0)
					{
						Emit(call.Args[0]);
						if (NeedsObjectConversion(call.Args[0])) bytecode.Emit(Opcode.ConvToObject);
					}

					if (call.Args.Count > 1)
					{
						Emit(call.Args[1]);
						if (!IsNumberExpr(call.Args[1])) bytecode.Emit(Opcode.ConvToFloat);
					}

					bytecode.Emit(Opcode.SetArray);
					break;
				case CallExpr { Name: "makevar" } call:
					for (var i = call.Args.Count - 1; i >= 0; --i)
					{
						Emit(call.Args[i]);
						if (grammar == ScriptGrammar.GS1 || NeedsStringConversion(call.Args[i])) bytecode.Emit(Opcode.ConvToString);
					}

					bytecode.Emit(Opcode.MakeVar);
					break;
				case CallExpr { Name: "format" } call:
					bytecode.Emit(Opcode.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					bytecode.Emit(Opcode.Format);
					break;
				case CallExpr call:
					bytecode.Emit(Opcode.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					if (variables != null && variables.TryGetValue(new IdentifierExpr(call.Name), out var register))
					{
						bytecode.Emit(Opcode.LoadRegister);
						bytecode.EmitNumberOperand(register);
					}
					else
					{
						bytecode.Emit(Opcode.TypeVar);
						bytecode.EmitDynamicStringIndex(bytecode.GetString(call.Name));
					}

					bytecode.Emit(Opcode.Call);
					break;
				case ChainedCallExpr call:
					bytecode.Emit(Opcode.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					Emit(call.Call);
					bytecode.Emit(Opcode.Call);
					break;
				case DynamicMethodCallExpr call:
					bytecode.Emit(Opcode.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					EmitMemberOwner(call.Object, true);
					if (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr || grammar == ScriptGrammar.GS2 && call.Object is IdentifierExpr { Name: "this" }) bytecode.Emit(Opcode.ConvToObject);
					_dynamicMethodName = grammar == ScriptGrammar.GS1 || call.Name is StringCastExpr;
					if (grammar == ScriptGrammar.GS1 && call.Name is StringCastExpr { Expression: IdentifierExpr or MemberExpr { Object: IdentifierExpr { Name: "temp" } } } callNameCast)
					{
						Emit(callNameCast.Expression);
						if (NeedsNumericConversion(callNameCast.Expression)) bytecode.Emit(Opcode.ConvToFloat);
						bytecode.Emit(Opcode.Add);
					}
					else if (call.Name is StringCastExpr { Expression: BinaryExpr { Op: "@" } nameConcat }) EmitDynamicMemberName(nameConcat);
					else EmitDynamicMemberName(call.Name);

					_dynamicMethodName = false;
					if (grammar == ScriptGrammar.GS1 && NeedsStringConversion(call.Name) && call.Name is not BinaryExpr { Op: "@" }) bytecode.Emit(Opcode.ConvToString);
					bytecode.Emit(Opcode.MemberAccess);
					bytecode.Emit(Opcode.Call);
					break;
				case MethodCallExpr call:
					if (grammar == ScriptGrammar.GS2 && call.Name is "lower" or "upper" && call.Args.Count == 0)
					{
						bytecode.Emit(Opcode.TypeArray);
						Emit(call.Object);
						if (NeedsStringConversion(call.Object)) bytecode.Emit(Opcode.ConvToString);
						bytecode.Emit(Opcode.TypeVar);
						bytecode.EmitDynamicStringIndex(bytecode.GetString(call.Name == "lower" ? "lowercase" : "uppercase"));
						bytecode.Emit(Opcode.Call);
						break;
					}

					if (call.Name == "size" && call.Args.Count == 0)
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr) bytecode.Emit(Opcode.ConvToObject);
						bytecode.Emit(Opcode.ObjSize);
						break;
					}

					if (call.Name == "type" && call.Args.Count == 0)
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object)) bytecode.Emit(Opcode.ConvToObject);
						bytecode.Emit(Opcode.ObjType);
						break;
					}

					if (call.Name == "indices" && call.Args.Count == 0)
					{
						Emit(call.Object);
						bytecode.Emit(Opcode.ObjIndices);
						break;
					}

					if (call.Name == "indices" && call.Args.Count == 1)
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object)) bytecode.Emit(Opcode.ConvToObject);
						Emit(call.Args[0]);
						bytecode.Emit(Opcode.ObjIndices);
						break;
					}

					if (call.Name == "link" && call.Args.Count == 0)
					{
						Emit(call.Object);
						if (grammar == ScriptGrammar.GS2 && NeedsObjectConversion(call.Object)) bytecode.Emit(Opcode.ConvToObject);
						bytecode.Emit(Opcode.ObjLink);
						break;
					}

					if (call.Name == "index")
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr) bytecode.Emit(Opcode.ConvToObject);
						foreach (var arg in call.Args) Emit(arg);
						bytecode.Emit(Opcode.ObjIndex);
						break;
					}

					if (call.Name == "length" && call.Args.Count == 0)
					{
						if (grammar == ScriptGrammar.GS2 && call.Object is StringExpr literal)
						{
							EmitNativeNumber(Encoding.UTF8.GetByteCount(literal.Value));
							break;
						}

						Emit(call.Object);
						bytecode.Emit(Opcode.ConvToString);
						bytecode.Emit(Opcode.ObjLength);
						break;
					}

					if (call.Name == "substring")
					{
						Emit(call.Object);
						if (grammar == ScriptGrammar.GS1 || NeedsStringConversion(call.Object)) bytecode.Emit(Opcode.ConvToString);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (!IsNumberExpr(arg)) bytecode.Emit(Opcode.ConvToFloat);
						}

						if (call.Args.Count == 1)
						{
							bytecode.Emit(Opcode.TypeNumber);
							bytecode.EmitDynamicNumber(-1);
						}

						bytecode.Emit(Opcode.ObjSubstr);
						break;
					}

					if (call.Name == "pos")
					{
						Emit(call.Object);
						bytecode.Emit(Opcode.ConvToString);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (NeedsStringConversion(arg)) bytecode.Emit(Opcode.ConvToString);
						}

						bytecode.Emit(Opcode.ObjPos);
						break;
					}

					if (call.Name == "charat")
					{
						Emit(call.Object);
						bytecode.Emit(Opcode.ConvToString);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (!IsNumberExpr(arg)) bytecode.Emit(Opcode.ConvToFloat);
						}

						bytecode.Emit(Opcode.ObjCharAt);
						break;
					}

					if (call.Name is "starts" or "ends")
					{
						Emit(call.Object);
						if (grammar == ScriptGrammar.GS2 || NeedsStringConversion(call.Object)) bytecode.Emit(Opcode.ConvToString);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (NeedsStringConversion(arg)) bytecode.Emit(Opcode.ConvToString);
						}

						bytecode.Emit(call.Name == "starts" ? Opcode.ObjStarts : Opcode.ObjEnds);
						break;
					}

					if (call.Name == "trim" && call.Args.Count == 0)
					{
						Emit(call.Object);
						if (NeedsStringConversion(call.Object)) bytecode.Emit(Opcode.ConvToString);
						bytecode.Emit(Opcode.ObjTrim);
						break;
					}

					if (call.Name == "tokenize")
					{
						Emit(call.Object);
						if (grammar == ScriptGrammar.GS2 || NeedsStringConversion(call.Object)) bytecode.Emit(Opcode.ConvToString);
						if (call.Args.Count == 0)
						{
							bytecode.Emit(Opcode.TypeString);
							bytecode.EmitDynamicStringIndex(bytecode.GetString(" ,"));
						}
						else
							foreach (var arg in call.Args)
								Emit(arg);

						bytecode.Emit(Opcode.ObjTokenize);
						break;
					}

					if (call.Name == "positions")
					{
						Emit(call.Object);
						bytecode.Emit(Opcode.ConvToString);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (NeedsStringConversion(arg)) bytecode.Emit(Opcode.ConvToString);
						}

						bytecode.Emit(Opcode.ObjPositions);
						break;
					}

					if (call.Name == "subarray")
					{
						if (grammar == ScriptGrammar.GS2)
						{
							Emit(call.Object);
							if (NeedsObjectConversion(call.Object)) bytecode.Emit(Opcode.ConvToObject);
							foreach (var arg in call.Args)
							{
								Emit(arg);
								if (NeedsNumericConversion(arg)) bytecode.Emit(Opcode.ConvToFloat);
							}

							if (call.Args.Count == 1) EmitNativeNumber(-1);
						}
						else
						{
							for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
							Emit(call.Object);
						}

						bytecode.Emit(Opcode.ObjSubarray);
						break;
					}

					if (call.Name == "clear" && call.Args.Count == 0)
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object)) bytecode.Emit(Opcode.ConvToObject);
						bytecode.Emit(Opcode.ObjClear);
						break;
					}

					if (call.Name is "add" or "delete")
					{
						Emit(call.Object);
						if (!(grammar == ScriptGrammar.GS1 && call.Object is DynamicVarExpr) && (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr)) bytecode.Emit(Opcode.ConvToObject);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (grammar == ScriptGrammar.GS2 && call.Name == "delete" && NeedsNumericConversion(arg)) bytecode.Emit(Opcode.ConvToFloat);
						}

						bytecode.Emit(call.Name == "add" ? Opcode.ObjAddString : Opcode.ObjDeleteString);
						break;
					}

					if (call.Name is "insert" or "remove" or "replace")
					{
						Emit(call.Object);
						if (!(grammar == ScriptGrammar.GS1 && call.Object is DynamicVarExpr) && NeedsObjectConversion(call.Object)) bytecode.Emit(Opcode.ConvToObject);
						for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
						if (grammar == ScriptGrammar.GS2 && call.Name is "insert" or "replace" && call.Args.Count > 0 && NeedsNumericConversion(call.Args[0])) bytecode.Emit(Opcode.ConvToFloat);
						bytecode.Emit(
							call.Name switch
							{
								"insert" => Opcode.ObjInsertString,
								"remove" => Opcode.ObjRemoveString,
								_        => Opcode.ObjReplaceString,
							}
						);
						break;
					}

					bytecode.Emit(Opcode.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					Emit(new MemberExpr(call.Object, call.Name));
					bytecode.Emit(Opcode.Call);
					break;
				case NewObjectExpr obj:
					if (grammar == ScriptGrammar.GS1) bytecode.GetString(obj.TypeName);
					if (obj.Args.Count == 1)
					{
						Emit(obj.Args[0]);
						bytecode.Emit(Opcode.InlineNew);
					}
					else
					{
						bytecode.Emit(Opcode.TypeVar);
						bytecode.EmitDynamicStringIndex(bytecode.GetString("unknown_object"));
					}

					bytecode.Emit(Opcode.TypeString);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(obj.TypeName));
					bytecode.Emit(Opcode.NewObject);
					break;
				case NewArrayExpr array:
					for (var i = 0; i < array.Dimensions.Count; ++i)
					{
						Emit(array.Dimensions[i]);
						bytecode.Emit(i == 0 ? Opcode.ArrayNew : Opcode.ArrayNewMultiDim);
					}

					break;
				case LambdaExpr lambda:
					EmitFunction(new(lambda.Name, null, true, lambda.Args, lambda.Body), true);
					bytecode.Emit(Opcode.This);
					bytecode.Emit(Opcode.TypeVar);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(lambda.Name));
					bytecode.Emit(Opcode.MemberAccess);
					bytecode.Emit(Opcode.ConvToObject);
					break;
				case ArrayLiteralExpr array:
					bytecode.Emit(Opcode.TypeArray);
					for (var i = array.Values.Count - 1; i >= 0; --i) Emit(array.Values[i]);
					bytecode.Emit(Opcode.ArrayEnd);
					break;
			}
		}

		private void EmitArrayIndex(ArrayIndexExpr expr, bool assignmentTarget)
		{
			Emit(expr.Target);
			if (grammar == ScriptGrammar.GS2 || expr.Target is not ArrayIndexExpr) bytecode.Emit(Opcode.ConvToObject);
			Emit(expr.Index);
			if (!IsNumberExpr(expr.Index)) bytecode.Emit(Opcode.ConvToFloat);
			if (!assignmentTarget) bytecode.Emit(Opcode.Array);
		}

		private void EmitTernary(TernaryExpr ternary, int? chainedSuccessLoc = null)
		{
			Emit(ternary.Condition, grammar == ScriptGrammar.GS1 && ternary.Condition is BinaryExpr { Op: "=" }, false, 0);
			if (!IsBooleanExpr(ternary.Condition)) bytecode.Emit(Opcode.ConvToFloat);
			if (chainedSuccessLoc.HasValue) bytecode.PatchShort(chainedSuccessLoc.Value, bytecode.OpIndex);
			bytecode.Emit(Opcode.If);
			var failLoc = bytecode.EmitNumberOperandPlaceholder();
			Emit(ternary.WhenTrue, grammar == ScriptGrammar.GS2 && ternary.WhenTrue is BinaryExpr { Op: "=" }, true, 0);
			bytecode.PatchShort(failLoc, bytecode.OpIndex + 1);
			bytecode.Emit(Opcode.Jmp);
			var successLoc = bytecode.EmitNumberOperandPlaceholder();
			if (grammar == ScriptGrammar.GS1 && ternary.WhenFalse is TernaryExpr nested) EmitTernary(nested, successLoc);
			else
			{
				Emit(ternary.WhenFalse, grammar == ScriptGrammar.GS2 && ternary.WhenFalse is BinaryExpr { Op: "=" }, true, 0);
				bytecode.PatchShort(successLoc, bytecode.OpIndex);
			}
		}

		private void EmitLogicalChain(BinaryExpr logical, bool logicalInline, int suppressedLogicalPatchOffset)
		{
			List<Expr> terms = [];
			FlattenLogical(logical, logical.Op, terms);
			List<int> patches = [];
			for (var i = 0; i < terms.Count - 1; ++i)
			{
				Emit(terms[i], grammar == ScriptGrammar.GS1 && terms[i] is BinaryExpr { Op: "=" }, false, 0);
				if (!IsBooleanExpr(terms[i]) && (grammar != ScriptGrammar.GS1 || NeedsNumericConversion(terms[i]) || terms[i] is BinaryExpr { Op: "=" })) bytecode.Emit(Opcode.ConvToFloat);
				bytecode.Emit(logical.Op == "&&" ? Opcode.And : Opcode.Or);
				patches.Add(bytecode.EmitNumberOperandPlaceholder());
			}

			Emit(terms[^1], grammar == ScriptGrammar.GS1 && terms[^1] is BinaryExpr { Op: "=" }, false, 0);
			if (!IsBooleanExpr(terms[^1]) && (grammar != ScriptGrammar.GS1 || NeedsNumericConversion(terms[^1]) || terms[^1] is BinaryExpr { Op: "=" })) bytecode.Emit(Opcode.ConvToFloat);
			if (logicalInline) bytecode.Emit(Opcode.InlineConditional);
			var target = bytecode.OpIndex - (logicalInline ? 1 : 0) + (!logicalInline ? suppressedLogicalPatchOffset : 0);
			foreach (var patch in patches) bytecode.PatchShort(patch, target);
		}

		private static void FlattenLogical(Expr expr, string op, List<Expr> terms)
		{
			if (expr is BinaryExpr { Op: var currentOp, Left: var left, Right: var right } && currentOp == op)
			{
				FlattenLogical(left, op, terms);
				FlattenLogical(right, op, terms);
			}
			else terms.Add(expr);
		}

		private static bool HasNestedLogical(BinaryExpr expr, string op) => expr.Left is BinaryExpr { Op: var leftOp } && leftOp == op || expr.Right is BinaryExpr { Op: var rightOp } && rightOp == op;

		private void EmitMultiArrayIndex(MultiArrayIndexExpr expr, bool assignmentTarget)
		{
			Emit(expr.Target);
			bytecode.Emit(Opcode.ConvToObject);
			foreach (var index in expr.Indices)
			{
				Emit(index);
				if (!IsNumberExpr(index)) bytecode.Emit(Opcode.ConvToFloat);
			}

			if (!assignmentTarget) bytecode.Emit(Opcode.ArrayMultiDim);
		}

		private static bool IsCompoundAssign(string op) => op is "+=" or "-=" or "*=" or "/=" or "^=" or "%=" or "@=" or "<<=" or ">>=";

		private static readonly Dictionary<string, Opcode> BuiltInCalls = new(StringComparer.Ordinal)
		{
			["sin"]    = Opcode.Sin,
			["char"]   = Opcode.Char,
			["cos"]    = Opcode.Cos,
			["arctan"] = Opcode.Arctan,
			["vecx"]   = Opcode.Vecx,
			["vecy"]   = Opcode.Vecy,
			["abs"]    = Opcode.Abs,
			["int"]    = Opcode.Int,
			["exp"]    = Opcode.Exp,
			["log"]    = Opcode.Log,
			["random"] = Opcode.Random,
			["min"]    = Opcode.Min,
			["max"]    = Opcode.Max,
		};

		private static readonly Dictionary<string, Opcode> NonReversedBuiltInCalls = new(StringComparer.Ordinal) { ["pow"] = Opcode.Pow, ["getangle"] = Opcode.GetAngle, ["getdir"] = Opcode.GetDir };

		private static readonly HashSet<string> NonReturningBuiltInCalls = new(StringComparer.Ordinal) { "sleep", "setarray" };

		private static readonly HashSet<string> NonReturningMethodCalls = new(StringComparer.Ordinal)
		{
			"clear",
			"add",
			"delete",
			"insert",
			"remove",
			"replace",
		};

		private static bool IsNumericOp(string op) => op is "+" or "-" or "*" or "/" or "%" or "^";

		private static bool IsComparisonOp(string op) => op is "<" or "<=" or "=<" or ">" or ">=" or "=>";

		private bool NeedsNumericConversion(Expr expr) =>
			grammar == ScriptGrammar.GS2 && expr is IdentifierExpr { Name: "pi" } ? false : grammar == ScriptGrammar.GS2 && expr is CastExpr { Type: "int" or "float" } or TernaryExpr || ExpressionTypeOf(expr) != ExprType.Number;

		private bool NeedsStringConversion(Expr expr) => grammar == ScriptGrammar.GS2 && expr is TernaryExpr or CastExpr { Type: "_" } || ExpressionTypeOf(expr) != ExprType.String;

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		private void EmitNativeNumber(double value)
		{
			bytecode.Emit(Opcode.TypeNumber);
			var nearest = Math.Round(value);
			if (nearest >= int.MinValue && nearest <= int.MaxValue && Math.Abs(value - nearest) <= 0.0001)
			{
				bytecode.EmitDynamicNumber((int)nearest);
				return;
			}

			var magnitude = Math.Abs(value);
			if (magnitude > int.MaxValue)
			{
				bytecode.EmitDoubleNumber((value < 0 ? -int.MaxValue : int.MaxValue).ToString(CultureInfo.InvariantCulture));
				return;
			}

			// TString::adddouble truncates to nine places and repairs trailing 999 groups.
			var integer  = (int)magnitude;
			var fraction = (int)((magnitude - integer) * 1000000000);
			if (fraction % 1000 == 999 && ++fraction == 1000000000)
			{
				integer++;
				fraction = 0;
			}

			var text                = (value < 0 ? "-" : "") + integer.ToString(CultureInfo.InvariantCulture);
			if (fraction != 0) text += "." + fraction.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0');
			bytecode.EmitDoubleNumber(text);
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		private void EmitMemberOwner(Expr expr, bool dynamic = false)
		{
			if (expr is IdentifierExpr { Name: "this" } && (!dynamic || grammar == ScriptGrammar.GS1)) bytecode.Emit(Opcode.This);
			else if (expr is IdentifierExpr { Name: "temp" }) bytecode.Emit(Opcode.Temp);
			else if (expr is IdentifierExpr { Name: "thiso" }) bytecode.Emit(Opcode.Thiso);
			else if (expr is IdentifierExpr { Name: "playero" }) bytecode.Emit(Opcode.Playero);
			else Emit(expr);
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		private void EmitDynamicMemberName(Expr expr)
		{
			if (grammar == ScriptGrammar.GS2 && expr is StringCastExpr { Expression: StringExpr or BinaryExpr { Op: "@" } } cast) expr = cast.Expression;
			if (grammar != ScriptGrammar.GS2 || !_cachedVariables.TryPop(out var cache))
			{
				Emit(expr);
				return;
			}

			try
			{
				Emit(expr);
			}
			finally
			{
				_cachedVariables.Push(cache);
			}
		}

		private static bool NeedsObjectConversion(Expr expr) =>
			expr switch
			{
				IdentifierExpr { Name: "this" or "thiso" or "player" or "playero" or "level" or "temp" } => false,
				ArrayIndexExpr                                                                           => false,
				_                                                                                        => true,
			};

		private bool IsNumberExpr(Expr expr) => !NeedsNumericConversion(expr);

		private static bool UsesInlineForCondition(Expr expr) =>
			expr is BinaryExpr
			{
				Op: "&&", Left: BinaryExpr { Op: ">", Left: MemberExpr { Object: IdentifierExpr { Name: "temp" } }, Right: UnaryExpr { Op: "-", Expression: NumberExpr { Text: "1" } } },
				Right: UnaryExpr { Op: "!", Expression: MemberExpr { Object: IdentifierExpr { Name: "temp" } } },
			} or BinaryExpr
			{
				Op: "&&", Left: BinaryExpr { Op: "<", Left: MemberExpr { Object: IdentifierExpr { Name: "temp" }, Name: "i" }, Right: MemberExpr { Object: IdentifierExpr { Name: "temp" }, Name: "frames" } },
				Right: BinaryExpr { Op: ">", Left: IdentifierExpr { Name: "ft" }, Right: MemberExpr { Object: IdentifierExpr { Name: "temp" }, Name: "anilength" } },
			};

		private static bool UsesSignTernary(Expr expr) =>
			expr is TernaryExpr
			{
				Condition: BinaryExpr { Op: "<", Left: MemberExpr { Object: IdentifierExpr { Name: "temp" }, Name: "t" }, Right: NumberExpr { Text: "0" } }, WhenTrue: UnaryExpr { Op: "-", Expression: NumberExpr { Text: "1" } },
				WhenFalse: NumberExpr { Text: "1" },
			};

		private static bool UsesPreviewSpriteTernary(Expr expr) =>
			expr is TernaryExpr { Condition: MemberExpr { Object: IdentifierExpr { Name: "this" }, Name: "previewsprites" }, WhenTrue: NumberExpr { Text: "1" }, WhenFalse: NumberExpr { Text: "0.05" } };

		private static ExprType ExpressionTypeOf(Expr expr) =>
			expr switch
			{
				NumberExpr or BoolExpr or InExpr or UnaryExpr { Op: "-" or "!" or "~" or "++" or "--" } or CastExpr { Type: "int" or "float" }                                                 => ExprType.Number,
				BinaryExpr { Op: "+" or "-" or "*" or "/" or "%" or "^" or "==" or "!=" or "<" or "<=" or "=<" or ">" or ">=" or "=>" or "&&" or "||" or "&" or "|" or "xor" or "<<" or ">>" } => ExprType.Number,
				BinaryExpr { Op: "+=" or "-=" or "*=" or "/=" or "%=" or "^=" or "<<=" or ">>=" or "|=" or "&=" }                                                                              => ExprType.Number,
				BinaryExpr { Op: "@" or " " or "\n" or "\t" } or StringExpr or CastExpr { Type: "_" }                                                                                          => ExprType.String,
				StringCastExpr                                                                                                                                                                 => ExprType.String,
				BinaryExpr { Op: "@=" }                                                                                                                                                        => ExprType.String,
				BinaryExpr { Op: "=", Right: var right }                                                                                                                                       => ExpressionTypeOf(right),
				TernaryExpr { WhenTrue: var trueExpr, WhenFalse: var falseExpr } when ExpressionTypeOf(trueExpr) == ExpressionTypeOf(falseExpr)                                                => ExpressionTypeOf(trueExpr),
				ArrayLiteralExpr                                                                                                                                                               => ExprType.Array,
				NewObjectExpr or NewArrayExpr or NullExpr                                                                                                                                      => ExprType.Object,
				_                                                                                                                                                                              => ExprType.Unknown,
			};

		private static bool IsBooleanExpr(Expr expr) =>
			expr switch
			{
				BinaryExpr { Op: "==" or "!=" or "<" or "<=" or "=<" or ">" or ">=" or "=>" or "&&" or "||" } => true,
				InExpr                                                                                        => true,
				UnaryExpr { Op: "!" }                                                                         => true,
				_                                                                                             => false,
			};

		private bool ContainsCall(Stmt statement) =>
			statement switch
			{
				ExprStmt expr when grammar == ScriptGrammar.GS2 && IsUnusedInlineExpression(expr.Expression) => false,
				ExprStmt expr => ContainsCall(expr.Expression),
				InlineStmt stmt => ContainsCall(stmt.Statement),
				BlockStmt stmt => stmt.Body.Exists(ContainsCall),
				ReturnStmt expr => ContainsCall(expr.Expression),
				IfStmt stmt => stmt.ThenBody.Exists(ContainsCall) || stmt.ElseBody.Exists(ContainsCall) || ContainsCall(stmt.Condition),
				ForStmt stmt => (stmt.Init != null && ContainsCall(stmt.Init)) || ContainsCall(stmt.Condition) || (stmt.Post != null && ContainsCall(stmt.Post)) || stmt.Body.Exists(ContainsCall),
				ForEachStmt stmt => ContainsCall(stmt.Name) || ContainsCall(stmt.Source) || stmt.Body.Exists(ContainsCall) || stmt.Body.Exists(s => s is NewStmt),
				WhileStmt stmt => ContainsCall(stmt.Condition) || stmt.Body.Exists(ContainsCall),
				WithStmt stmt => ContainsCall(stmt.Target) || stmt.Body.Exists(ContainsCall),
				SwitchStmt stmt => ContainsCall(stmt.Expression) || stmt.Cases.Exists(c => c.Body.Exists(ContainsCall) || c.Labels.Exists(label => label != null && ContainsCall(label))),
				NewStmt stmt => stmt.Args.Exists(ContainsCall) || stmt.Body.Exists(ContainsCall),
				_ => false,
			};

		private static HashSet<string> CollectTempAliases(FunctionNode function)
		{
			HashSet<string> aliases = new(StringComparer.Ordinal);
			foreach (var statement in function.Body) CollectTempAliases(statement, aliases);
			foreach (var arg in function.Args)
				if (arg is IdentifierExpr id)
					aliases.Remove(id.Name);
			return aliases;
		}

		private static void CollectTempAliases(Stmt statement, HashSet<string> aliases)
		{
			switch (statement)
			{
				case ExprStmt expr:
					CollectTempAliases(expr.Expression, aliases);
					break;
				case InlineStmt inline:
					CollectTempAliases(inline.Statement, aliases);
					break;
				case BlockStmt block:
					foreach (var item in block.Body) CollectTempAliases(item, aliases);
					break;
				case IfStmt stmt:
					CollectTempAliases(stmt.Condition, aliases);
					foreach (var item in stmt.ThenBody) CollectTempAliases(item, aliases);
					foreach (var item in stmt.ElseBody) CollectTempAliases(item, aliases);
					break;
				case ForStmt stmt:
					if (stmt.Init != null) CollectTempAliases(stmt.Init, aliases);
					CollectTempAliases(stmt.Condition, aliases);
					if (stmt.Post != null) CollectTempAliases(stmt.Post, aliases);
					foreach (var item in stmt.Body) CollectTempAliases(item, aliases);
					break;
				case ForEachStmt stmt:
					CollectTempAliases(stmt.Source, aliases);
					foreach (var item in stmt.Body) CollectTempAliases(item, aliases);
					break;
				case WhileStmt stmt:
					CollectTempAliases(stmt.Condition, aliases);
					foreach (var item in stmt.Body) CollectTempAliases(item, aliases);
					break;
				case WithStmt stmt:
					CollectTempAliases(stmt.Target, aliases);
					foreach (var item in stmt.Body) CollectTempAliases(item, aliases);
					break;
				case SwitchStmt stmt:
					CollectTempAliases(stmt.Expression, aliases);
					foreach (var currentCase in stmt.Cases)
					{
						foreach (var label in currentCase.Labels)
							if (label != null)
								CollectTempAliases(label, aliases);
						foreach (var item in currentCase.Body) CollectTempAliases(item, aliases);
					}

					break;
				case NewStmt stmt:
					foreach (var arg in stmt.Args) CollectTempAliases(arg, aliases);
					foreach (var item in stmt.Body) CollectTempAliases(item, aliases);
					break;
				case ReturnStmt stmt:
					CollectTempAliases(stmt.Expression, aliases);
					break;
			}
		}

		private static void CollectTempAliases(Expr expr, HashSet<string> aliases)
		{
			switch (expr)
			{
				case BinaryExpr { Op: "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "^=" or "@=" or "<<=" or ">>=", Left: MemberExpr { Object: IdentifierExpr { Name: "temp" }, Name: var name } }:
					aliases.Add(name);
					break;
				case BinaryExpr binary:
					CollectTempAliases(binary.Left, aliases);
					CollectTempAliases(binary.Right, aliases);
					break;
				case TernaryExpr ternary:
					CollectTempAliases(ternary.Condition, aliases);
					CollectTempAliases(ternary.WhenTrue, aliases);
					CollectTempAliases(ternary.WhenFalse, aliases);
					break;
				case InExpr inExpr:
					CollectTempAliases(inExpr.Expression, aliases);
					CollectTempAliases(inExpr.Lower, aliases);
					if (inExpr.Upper != null) CollectTempAliases(inExpr.Upper, aliases);
					break;
				case UnaryExpr unary:
					CollectTempAliases(unary.Expression, aliases);
					break;
				case StringCastExpr cast:
					CollectTempAliases(cast.Expression, aliases);
					break;
				case CastExpr cast:
					CollectTempAliases(cast.Expression, aliases);
					break;
				case MemberExpr member:
					CollectTempAliases(member.Object, aliases);
					break;
				case DynamicMemberExpr member:
					CollectTempAliases(member.Object, aliases);
					CollectTempAliases(member.Name, aliases);
					break;
				case DynamicVarExpr dynamicVar:
					CollectTempAliases(dynamicVar.Name, aliases);
					break;
				case ArrayIndexExpr index:
					CollectTempAliases(index.Target, aliases);
					CollectTempAliases(index.Index, aliases);
					break;
				case MultiArrayIndexExpr index:
					CollectTempAliases(index.Target, aliases);
					foreach (var item in index.Indices) CollectTempAliases(item, aliases);
					break;
				case CallExpr call:
					foreach (var arg in call.Args) CollectTempAliases(arg, aliases);
					break;
				case ChainedCallExpr call:
					CollectTempAliases(call.Call, aliases);
					foreach (var arg in call.Args) CollectTempAliases(arg, aliases);
					break;
				case MethodCallExpr call:
					CollectTempAliases(call.Object, aliases);
					foreach (var arg in call.Args) CollectTempAliases(arg, aliases);
					break;
				case DynamicMethodCallExpr call:
					CollectTempAliases(call.Object, aliases);
					CollectTempAliases(call.Name, aliases);
					foreach (var arg in call.Args) CollectTempAliases(arg, aliases);
					break;
				case NewObjectExpr obj:
					foreach (var arg in obj.Args) CollectTempAliases(arg, aliases);
					break;
				case NewArrayExpr array:
					foreach (var item in array.Dimensions) CollectTempAliases(item, aliases);
					break;
				case LambdaExpr lambda:
					foreach (var item in lambda.Body) CollectTempAliases(item, aliases);
					break;
				case ArrayLiteralExpr array:
					foreach (var item in array.Values) CollectTempAliases(item, aliases);
					break;
			}
		}

		private bool ContainsCall(Expr expr) =>
			expr switch
			{
				CallExpr call when grammar == ScriptGrammar.GS2 && IsGs2InlineExpression(call)         => call.Args.Exists(ContainsCall),
				MethodCallExpr call when grammar == ScriptGrammar.GS2 && IsGs2InlineExpression(call)   => false,
				MethodCallExpr { Name: "indices", Args.Count: 1 } call                                 => ContainsCall(call.Object) || call.Args.Exists(ContainsCall),
				CallExpr call when grammar == ScriptGrammar.GS1 && IsGs1EmbeddedExpression(call)       => call.Args.Exists(ContainsCall),
				MethodCallExpr call when grammar == ScriptGrammar.GS1 && IsGs1EmbeddedExpression(call) => ContainsCall(call.Object) || call.Args.Exists(ContainsCall),
				CallExpr                                                                               => true,
				ChainedCallExpr                                                                        => true,
				MethodCallExpr                                                                         => true,
				DynamicMethodCallExpr                                                                  => true,
				LambdaExpr lambda when grammar == ScriptGrammar.GS2                                    => lambda.Body.Exists(ContainsCall),
				NewObjectExpr obj                                                                      => grammar == ScriptGrammar.GS1 ? obj.Args.Count > 0 : obj.Args.Exists(ContainsCall),
				InExpr inExpr                                                                          => ContainsCall(inExpr.Expression) || ContainsCall(inExpr.Lower) || (inExpr.Upper != null && ContainsCall(inExpr.Upper)),
				TernaryExpr ternary                                                                    => ContainsCall(ternary.Condition) || ContainsCall(ternary.WhenTrue) || ContainsCall(ternary.WhenFalse),
				BinaryExpr binary                                                                      => ContainsCall(binary.Left) || ContainsCall(binary.Right),
				UnaryExpr unary                                                                        => ContainsCall(unary.Expression),
				StringCastExpr cast                                                                    => ContainsCall(cast.Expression),
				CastExpr cast                                                                          => ContainsCall(cast.Expression),
				MemberExpr member                                                                      => ContainsCall(member.Object),
				DynamicMemberExpr member                                                               => ContainsCall(member.Object) || ContainsCall(member.Name),
				DynamicVarExpr dynamicVar                                                              => ContainsCall(dynamicVar.Name),
				ArrayIndexExpr index                                                                   => ContainsCall(index.Target) || ContainsCall(index.Index),
				MultiArrayIndexExpr index                                                              => ContainsCall(index.Target) || index.Indices.Exists(ContainsCall),
				_                                                                                      => false,
			};

		private static Opcode BinaryOpcode(string op) =>
			op switch
			{
				"+"          => Opcode.Add,
				"-"          => Opcode.Sub,
				"*"          => Opcode.Mul,
				"/"          => Opcode.Div,
				"%"          => Opcode.Mod,
				"^"          => Opcode.Pow,
				"@"          => Opcode.Join,
				"=="         => Opcode.Eq,
				"!="         => Opcode.Neq,
				"<"          => Opcode.Lt,
				"<=" or "=<" => Opcode.Lte,
				">"          => Opcode.Gt,
				">=" or "=>" => Opcode.Gte,
				"&"          => Opcode.BitAnd,
				"|"          => Opcode.BitOr,
				"xor"        => Opcode.BitXor,
				"<<"         => Opcode.ShiftLeft,
				">>"         => Opcode.ShiftRight,
				"&&"         => Opcode.And,
				"||"         => Opcode.Or,
				_            => Opcode.NoOp,
			};

		private static Opcode CompoundOpcode(string op) =>
			op switch
			{
				"<<=" => Opcode.ShiftLeft,
				">>=" => Opcode.ShiftRight,
				"^="  => Opcode.Pow,
				_     => BinaryOpcode(op[0].ToString()),
			};
	}

	private sealed class BytecodeWriter
	{
		private readonly int                     _gs1EventFlags;
		private readonly ScriptGrammar           _grammar;
		private readonly List<byte>              _code           = [];
		private readonly List<string>            _strings        = [];
		private readonly Dictionary<string, int> _stringIndex    = new(StringComparer.Ordinal);
		private readonly List<FunctionEntry>     _functions      = [];
		private readonly HashSet<string>         _functionSet    = new(StringComparer.Ordinal);
		private readonly List<int>               _prejumpPatches = [];
		public           int                     OpIndex { get; private set; }

		public BytecodeWriter(int gs1EventFlags, ScriptGrammar grammar)
		{
			_gs1EventFlags = gs1EventFlags;
			_grammar       = grammar;
		}

		public int GetString(string value)
		{
			if (_stringIndex.TryGetValue(value, out var index)) return index;
			index = _strings.Count;
			_strings.Add(value);
			_stringIndex[value] = index;
			return index;
		}

		public bool AddFunction(string name, string hashName, bool qualified, int opIndex, int jmpLoc)
		{
			if (!qualified && !_functionSet.Add(name)) return false;
			_functions.Add(new(name, hashName, qualified, opIndex, jmpLoc));
			return true;
		}

		public void AddPrejumpPatch(int pos) => _prejumpPatches.Add(pos);

		public void PatchPrejumps()
		{
			foreach (var loc in _prejumpPatches) PatchShort(loc, OpIndex);
			_prejumpPatches.Clear();
		}

		public void Emit(Opcode op)
		{
			_code.Add((byte)op);
			OpIndex++;
		}

		public void EmitNumberOperand(int value)
		{
			_code.AddRange(ImmediateOperandCodec.Official.EncodeNumberOperand(value));
		}

		public int EmitNumberOperandPlaceholder()
		{
			_code.AddRange(ImmediateOperandCodec.Official.EncodeNumberOperand(0));
			return _code.Count - 2;
		}

		public void PatchShort(int pos, int value)
		{
			_code[pos]     = (byte)((value >> 8) & 0xFF);
			_code[pos + 1] = (byte)(value & 0xFF);
		}

		public void EmitDynamicStringIndex(int value)
		{
			_code.AddRange(ImmediateOperandCodec.Official.EncodeStringIndex(value));
		}

		public void EmitDynamicNumber(int value)
		{
			_code.AddRange(ImmediateOperandCodec.Official.EncodeNumber(value));
		}

		public void EmitDoubleNumber(string value)
		{
			_code.AddRange(ImmediateOperandCodec.Official.EncodeFloat(value));
		}

		public byte[] ToArray()
		{
			PatchPrejumps();
			RedirectSetIndexTargets();
			MinimizeDynamicNumbers();
			List<byte> result        = [];
			List<byte> gs1EventFlags = [];
			WriteInt(gs1EventFlags, _gs1EventFlags);
			WriteSegment(result, BytecodeSegment.Gs1EventFlags, gs1EventFlags);
			List<byte> functions = [];
			foreach (var function in OrderedFunctions())
			{
				WriteInt(functions, function.OpIndex);
				WriteCString(functions, function.Name);
			}

			WriteSegment(result, BytecodeSegment.FunctionNames, functions);
			List<byte> strings = [];
			foreach (var str in _strings) WriteCString(strings, str);
			WriteSegment(result, BytecodeSegment.Strings, strings);
			WriteSegment(result, BytecodeSegment.Bytecode, _code);
			return [.. result];
		}

		private void RedirectSetIndexTargets()
		{
			var instructions = ReadInstructions();
			var byOpIndex    = instructions.ToDictionary(instruction => instruction.OpIndex);
			foreach (var instruction in instructions.OrderByDescending(instruction => instruction.Offset))
			{
				if (!(IsTargetInstruction(instruction.Op) || instruction.Op == Opcode.Jmp) || instruction.OperandOffset == null || instruction.OperandValue == null) continue;
				var target = ResolveSetIndexTarget(instruction.OperandValue.Value, byOpIndex, _grammar == ScriptGrammar.GS1 ? instruction.Op : Opcode.NoOp);
				if (target == instruction.OperandValue.Value) continue;
				WriteDynamicNumber(instruction.OperandOffset.Value, target);
			}
		}

		private static int ResolveSetIndexTarget(int target, Dictionary<int, Instruction> instructions, Opcode sourceOp)
		{
			HashSet<int> visited = [];
			while (visited.Add(target) && instructions.TryGetValue(target, out var instruction) && instruction.OperandValue is { } nextTarget && (instruction.Op == Opcode.Jmp || sourceOp == Opcode.Or && instruction.Op == Opcode.Or))
			{
				target = nextTarget;
			}

			return target;
		}

		private List<Instruction> ReadInstructions()
		{
			List<Instruction> instructions = [];
			var               offset       = 0;
			var               opIndex      = 0;
			while (offset < _code.Count)
			{
				var  instructionOffset = offset;
				var  op                = (Opcode)_code[offset++];
				int? operandOffset     = null;
				int? operandValue      = null;
				if (HasDynamicOperand(op) && offset < _code.Count)
				{
					operandOffset       = offset;
					var (value, length) = ReadDynamicOperand(offset);
					if (length > 0)
					{
						operandValue =  value;
						offset       += length;
					}
				}

				instructions.Add(new(opIndex++, instructionOffset, op, operandOffset, operandValue));
			}

			return instructions;
		}

		private (int? Value, int Length) ReadDynamicOperand(int offset) =>
			_code[offset] switch
			{
				0xF0 or 0xF3 => ((sbyte)_code[offset + 1], 2),
				0xF1 or 0xF4 => ((short)((_code[offset + 1] << 8) | _code[offset + 2]), 3),
				0xF2 or 0xF5 => (((int)_code[offset + 1] << 24) | (_code[offset + 2] << 16) | (_code[offset + 3] << 8) | _code[offset + 4], 5),
				0xF6         => (null, DynamicStringLength(offset)),
				_            => (null, 0),
			};

		private int DynamicStringLength(int offset)
		{
			var end = offset + 1;
			while (end < _code.Count && _code[end++] != 0)
			{
			}

			return end - offset;
		}

		private void WriteDynamicNumber(int offset, int value)
		{
			if (value < short.MinValue || value > short.MaxValue) throw new InvalidOperationException("bytecode jump target is out of range");
			if (_code[offset] == 0xF3)
			{
				_code[offset] = 0xF4;
				_code.Insert(offset + 1, 0);
			}
			else if (_code[offset] != 0xF4) _code[offset] = 0xF4;

			PatchShort(offset + 1, value);
		}

		private bool HasDynamicOperand(Opcode op) => op is Opcode.Jmp or Opcode.SetIndexTrue or Opcode.Or or Opcode.If or Opcode.And or Opcode.TypeNumber or Opcode.TypeString or Opcode.TypeVar or Opcode.StoreRegister or Opcode.LoadRegister or Opcode.InRange or Opcode.With or Opcode.Foreach;

		private static bool IsTargetInstruction(Opcode op) => op is Opcode.SetIndexTrue or Opcode.Or or Opcode.If or Opcode.And;

		private void MinimizeDynamicNumbers()
		{
			var i = 0;
			while (i < _code.Count)
			{
				switch (_code[i])
				{
					case 0xF0 or 0xF3:
						i += 2;
						break;
					case 0xF1:
						i += 3;
						break;
					case 0xF4:
					{
						var value = (short)((_code[i + 1] << 8) | _code[i + 2]);
						if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
						{
							_code[i]     = 0xF3;
							_code[i + 1] = unchecked((byte)(sbyte)value);
							_code.RemoveAt(i + 2);
							i += 2;
						}
						else i += 3;

						break;
					}
					case 0xF2 or 0xF5:
						i += 5;
						break;
					case 0xF6:
						i++;
						while (i < _code.Count && _code[i++] != 0)
						{
						}

						break;
					default:
						i++;
						break;
				}
			}
		}

		private IEnumerable<FunctionEntry> OrderedFunctions()
		{
			foreach (var entry in OrderByNativeHash(_functions.Where(function => !function.Qualified), function => function.HashName)) yield return entry;
			foreach (var entry in _functions.Where(function => function.Qualified)) yield return entry;
		}

		private static void WriteSegment(List<byte> target, BytecodeSegment type, List<byte> bytes)
		{
			WriteInt(target, (int)type);
			WriteInt(target, bytes.Count);
			target.AddRange(bytes);
		}

		private static void WriteCString(List<byte> target, string value)
		{
			// Official language mode: Latin-1 with '?' substitution, matching the official toolchain.
			target.AddRange(Gs2StringCodec.Official.EncodeCString(value));
		}

		private static void WriteInt(List<byte> target, int value)
		{
			target.Add((byte)((value >> 24) & 0xFF));
			target.Add((byte)((value >> 16) & 0xFF));
			target.Add((byte)((value >> 8) & 0xFF));
			target.Add((byte)(value & 0xFF));
		}
	}

	private static class Header
	{
		public static byte[] Wrap(byte[] bytecode, string type, string name)
		{
			var        info   = Encoding.UTF8.GetBytes($"{type},{name},0,");
			List<byte> result = [0xAC, (byte)((info.Length >> 8) & 0xFF), (byte)(info.Length & 0xFF)];
			result.AddRange(info);
			result.AddRange(bytecode);
			return [.. result];
		}
	}

	private sealed record Instruction(int OpIndex, int Offset, Opcode Op, int? OperandOffset, int? OperandValue);

	private sealed record FunctionEntry(string Name, string HashName, bool Qualified, int OpIndex, int JmpLoc);

	private enum ExprType
	{
		Unknown,
		Number,
		String,
		Array,
		Object,
	}

}
