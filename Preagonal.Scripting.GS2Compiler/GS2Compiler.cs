using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Preagonal.Scripting.GS2Compiler.Enums;
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
			_ => AntlrGs2Parser.Parse(code),
		};
		if (parseResult.ErrorMessage != null || parseResult.Program == null) return new() { Success = false, ErrMsg = parseResult.ErrorMessage ?? "malformed input\n", ByteCode = [] };
		var program = parseResult.Program;
		var bytecode = new BytecodeWriter(program.Gs1EventFlags);
		var emitter = new Emitter(bytecode, program.Constants, program.Enums);
		foreach (var item in program.Items)
		{
			if (item is FunctionItem function) emitter.EmitFunction(function.Function);
			else if (item is StatementItem statement) emitter.EmitStatement(statement.Statement);
		}
		var result = bytecode.ToArray();
		return new() { Success = true, ErrMsg = null, ByteCode = withHeader ? Header.Wrap(result, type, name) : result };
	}

	private sealed record ParseResult(ProgramNode? Program, string? ErrorMessage);

	private sealed class ParseFailure(int line) : Exception
	{
		public int Line { get; } = line;
	}

	private sealed class ThrowingErrorListener : IAntlrErrorListener<int>, IAntlrErrorListener<IToken>
	{
		public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e) => throw new ParseFailure(line);
		public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e) => throw new ParseFailure(line);
	}

	private static string ErrorMessage(string code, int line) => $"malformed input at line {line}: {LineText(code, line)}\n";

	private static string LineText(string code, int line)
	{
		var currentLine = 1;
		var start = 0;
		while (currentLine < line && start < code.Length)
		{
			var next = code.IndexOf('\n', start);
			if (next < 0) return "";
			start = next + 1;
			currentLine++;
		}
		var end = code.IndexOf('\n', start);
		if (end < 0) end = code.Length;
		if (end > start && code[end - 1] == '\r') end--;
		return code[start..end];
	}

	private static string NormalizeOperator(string op) => op switch
	{
		":=" => "=",
		"<>" => "!=",
		_ => op,
	};

	private static class AntlrGs2Parser
	{
		public static ParseResult Parse(string code)
		{
			try
			{
				var input = CharStreams.fromString(code);
				var lexer = new GS2Lexer(input);
				var tokens = new CommonTokenStream(lexer);
				var parser = new GS2Parser(tokens);
				var listener = new ThrowingErrorListener();
				lexer.RemoveErrorListeners();
				parser.RemoveErrorListeners();
				lexer.AddErrorListener(listener);
				parser.AddErrorListener(listener);
				var script = parser.script();
				return parser.NumberOfSyntaxErrors == 0
					? new(new Builder().Build(script), null)
					: new(null, ErrorMessage(code, 1));
			}
			catch (ParseFailure ex)
			{
				return new(null, ErrorMessage(code, ex.Line));
			}
		}

		private sealed class Builder
		{
			private readonly Dictionary<string, Expr> _constants = new(StringComparer.Ordinal);
			private readonly Dictionary<string, Dictionary<string, int>> _enums = new(StringComparer.Ordinal);
			private int _lambdaId = 100;

			public ProgramNode Build(GS2Parser.ScriptContext context)
			{
				List<ProgramItem> items = [];
				var gs1EventFlags = 0;
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
				var enumName = context.IDENTIFIER().GetText();
				Dictionary<string, int> values = new(StringComparer.Ordinal);
				var index = 0;
				foreach (var member in context.enumMember())
				{
					if (member.expression() != null) index = ParseEnumValue(member.expression());
					values[member.IDENTIFIER().GetText()] = index++;
				}
				_enums[enumName] = values;
			}

			private int ParseEnumValue(GS2Parser.ExpressionContext context) => BuildExpression(context) switch
			{
				NumberExpr number => int.Parse(number.Text, CultureInfo.InvariantCulture),
				UnaryExpr { Op: "-", Expression: NumberExpr number } => -int.Parse(number.Text, CultureInfo.InvariantCulture),
				_ => 0,
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
				if (context.whileStatement() is { } whileStatement) return new WhileStmt(BuildExpression(whileStatement.expression()), BuildStatementBody(whileStatement.statement()));
				if (context.switchStatement() is { } switchStatement) return BuildSwitch(switchStatement);
				if (context.withStatement() is { } withStatement) return new WithStmt(BuildExpression(withStatement.expression()), BuildStatementBody(withStatement.statement()));
				if (context.newStatement() is { } newStatement) return new NewStmt(newStatement.IDENTIFIER().GetText(), BuildArguments(newStatement.argumentList()), newStatement.block() == null ? [] : BuildBlock(newStatement.block()));
				if (context.returnStatement() is { } returnStatement) return new ReturnStmt(returnStatement.expression() == null ? new NumberExpr("0") : BuildExpression(returnStatement.expression()));
				if (context.breakStatement() != null) return new BreakStmt();
				if (context.continueStatement() != null) return new ContinueStmt();
				if (context.expressionStatement() is { } expressionStatement) return new ExprStmt(BuildExpression(expressionStatement.expression()));
				throw Failure(context);
			}

			private static bool IsEmptyStatement(GS2Parser.StatementContext context) => context.SEMI() != null && context.ChildCount == 1;

			private IfStmt BuildIf(GS2Parser.IfStatementContext context)
			{
				var hasElse = context.ELSE() != null || context.ELSEIF() != null;
				List<Stmt> elseBody = [];
				if (context.ELSE() != null) elseBody = BuildStatementBody(context.statement(1));
				else if (context.ifTail() != null) elseBody = [BuildIfTail(context.ifTail())];
				return new(BuildExpression(context.expression()), BuildStatementBody(context.statement(0)), elseBody, hasElse);
			}

			private IfStmt BuildIfTail(GS2Parser.IfTailContext context)
			{
				var hasElse = context.ELSE() != null || context.ELSEIF() != null;
				List<Stmt> elseBody = [];
				if (context.ELSE() != null) elseBody = BuildStatementBody(context.statement(1));
				else if (context.ifTail() != null) elseBody = [BuildIfTail(context.ifTail())];
				return new(BuildExpression(context.expression()), BuildStatementBody(context.statement(0)), elseBody, hasElse);
			}

			private Stmt BuildFor(GS2Parser.ForStatementContext context)
			{
				var expressions = context.expression();
				if (context.COLON() != null) return new ForEachStmt(BuildExpression(expressions[0]), BuildExpression(expressions[1]), BuildStatementBody(context.statement()));
				Expr? init = null;
				Expr? post = null;
				var expressionIndex = 0;
				if (expressions.Length > expressionIndex && expressions[expressionIndex].Start.TokenIndex < context.SEMI(0).Symbol.TokenIndex) init = BuildExpression(expressions[expressionIndex++]);
				var condition = expressions.Length > expressionIndex && expressions[expressionIndex].Start.TokenIndex < context.SEMI(1).Symbol.TokenIndex ? BuildExpression(expressions[expressionIndex++]) : new BoolExpr(true);
				if (expressions.Length > expressionIndex) post = BuildExpression(expressions[expressionIndex]);
				return new ForStmt(init, condition, post, BuildStatementBody(context.statement()));
			}

			private SwitchStmt BuildSwitch(GS2Parser.SwitchStatementContext context)
			{
				List<SwitchCase> cases = [];
				List<Expr?> labels = [];
				foreach (var switchCase in context.switchCase())
				{
					labels.Add(switchCase.DEFAULT() != null ? null : BuildExpression(switchCase.expression()));
					var statements = switchCase.statement().Select(BuildStatement).ToList();
					if (statements.Count == 0) continue;
					if (labels.Count > 1) labels.Reverse();
					cases.Add(new([.. labels], statements));
					labels.Clear();
				}
				if (labels.Count > 0)
				{
					if (labels.Count > 1) labels.Reverse();
					cases.Add(new([.. labels], []));
				}
				return new(BuildExpression(context.expression()), cases);
			}

			private Expr BuildExpression(GS2Parser.ExpressionContext context) => BuildAssignment(context.assignmentExpression());

			private Expr BuildAssignment(GS2Parser.AssignmentExpressionContext context)
			{
				var left = BuildConditional(context.conditionalExpression());
				return context.assignmentOperator() == null ? left : new BinaryExpr(left, NormalizeOperator(context.assignmentOperator().GetText()), BuildAssignment(context.assignmentExpression()));
			}

			private Expr BuildConditional(GS2Parser.ConditionalExpressionContext context)
			{
				var condition = BuildLogicalOr(context.logicalOrExpression());
				return context.QUESTION() == null ? condition : new TernaryExpr(condition, BuildExpression(context.expression(0)), BuildExpression(context.expression(1)));
			}

			private Expr BuildLogicalOr(GS2Parser.LogicalOrExpressionContext context) => BuildBinaryChain(context, context.logicalAndExpression(), BuildLogicalAnd);
			private Expr BuildLogicalAnd(GS2Parser.LogicalAndExpressionContext context) => BuildBinaryChain(context, context.bitwiseExpression(), BuildBitwise);
			private Expr BuildBitwise(GS2Parser.BitwiseExpressionContext context) => BuildBinaryChain(context, context.equalityExpression(), BuildEquality);

			private Expr BuildEquality(GS2Parser.EqualityExpressionContext context)
			{
				var comparisons = context.comparisonExpression();
				var ranges = context.rangeExpression();
				var comparisonIndex = 1;
				var rangeIndex = 0;
				var expression = BuildComparison(comparisons[0]);
				for (var i = 1; i < context.ChildCount; ++i)
				{
					if (context.GetChild(i) is not ITerminalNode terminal) continue;
					if (terminal.Symbol.Type == GS2Parser.IN) expression = BuildInRange(expression, ranges[rangeIndex++]);
					else expression = new BinaryExpr(expression, NormalizeOperator(terminal.GetText()), BuildComparison(comparisons[comparisonIndex++]));
				}
				return expression;
			}

			private Expr BuildInRange(Expr left, GS2Parser.RangeExpressionContext context)
			{
				if (context.comparisonExpression() != null) return new InExpr(left, BuildComparison(context.comparisonExpression()), null);
				var expressions = context.expression();
				return new InExpr(left, BuildExpression(expressions[0]), expressions.Length > 1 ? BuildExpression(expressions[1]) : null);
			}

			private Expr BuildComparison(GS2Parser.ComparisonExpressionContext context) => BuildBinaryChain(context, context.concatExpression(), BuildConcat);
			private Expr BuildConcat(GS2Parser.ConcatExpressionContext context) => BuildBinaryChain(context, context.additiveExpression(), BuildAdditive);
			private Expr BuildAdditive(GS2Parser.AdditiveExpressionContext context) => BuildBinaryChain(context, context.multiplicativeExpression(), BuildMultiplicative);
			private Expr BuildMultiplicative(GS2Parser.MultiplicativeExpressionContext context) => BuildBinaryChain(context, context.prefixExpression(), BuildPrefix);

			private Expr BuildPrefix(GS2Parser.PrefixExpressionContext context)
			{
				if (context.postfixExpression() != null) return BuildPostfix(context.postfixExpression());
				var op = context.op.Text;
				var expression = BuildPrefix(context.prefixExpression());
				return op switch
				{
					"@" => new StringCastExpr(expression),
					_ => new UnaryExpr(op, expression),
				};
			}

			private Expr BuildPostfix(GS2Parser.PostfixExpressionContext context)
			{
				var expression = BuildPrimary(context.primaryExpression());
				foreach (var part in context.postfixPart())
				{
					if (part.DOT() != null && part.IDENTIFIER() != null)
					{
						expression = new MemberExpr(expression, part.IDENTIFIER().GetText());
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
					var args = BuildArguments(part.argumentList());
					expression = expression switch
					{
						IdentifierExpr id => new CallExpr(id.Name, args),
						MemberExpr member => new MethodCallExpr(member.Object, member.Name, args),
						DynamicMemberExpr member => new DynamicMethodCallExpr(member.Object, member.Name, args),
						CallExpr call => new ChainedCallExpr(call, args),
						_ => expression,
					};
				}
				return expression;
			}

			private Expr BuildDynamicMemberName(GS2Parser.ExpressionContext context)
			{
				var expression = BuildExpression(context);
				return context.Start.Type == GS2Parser.CONCAT ? new StringCastExpr(RemoveLeadingStringCast(expression)) : expression;
			}

			private static Expr RemoveLeadingStringCast(Expr expression) => expression switch
			{
				StringCastExpr cast => cast.Expression,
				BinaryExpr { Left: StringCastExpr cast, Op: var op, Right: var right } => new BinaryExpr(cast.Expression, op, right),
				_ => expression,
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
				var parts = text.Split("::", StringSplitOptions.None);
				return parts.Length == 2 && !parts[0].StartsWith('$') ? new EnumExpr(parts[0], parts[1]) : new IdentifierExpr(text);
			}

			private CastExpr BuildCast(GS2Parser.CastExpressionContext context)
			{
				var type = context.INT_CAST() != null ? "int" : context.FLOAT_CAST() != null ? "float" : "_";
				return new(type, BuildExpression(context.expression()));
			}

			private LambdaExpr BuildLambda(GS2Parser.LambdaExpressionContext context) =>
				new($"function_{_lambdaId++}_1", BuildArguments(context.argumentList()), BuildStatementBody(context.statement()));

			private Expr BuildNewExpression(GS2Parser.NewExpressionContext context) =>
				context.IDENTIFIER() != null ? new NewObjectExpr(context.IDENTIFIER().GetText(), BuildArguments(context.argumentList())) : new NewArrayExpr(context.arrayRank().Select(rank => BuildExpression(rank.expression())).ToList());

			private static Expr ApplyIndices(Expr expression, List<Expr> indices, bool zeroWhenEmpty) => indices.Count switch
			{
				0 => zeroWhenEmpty ? new ArrayIndexExpr(expression, new NumberExpr("0")) : expression,
				1 => new ArrayIndexExpr(expression, indices[0]),
				_ => new MultiArrayIndexExpr(expression, indices),
			};

			private List<Expr> BuildArguments(GS2Parser.ArgumentListContext? context) => context == null ? [] : context.expression().Select(BuildExpression).ToList();

			private static Expr BuildBinaryChain<TContext>(ParserRuleContext context, TContext[] operands, Func<TContext, Expr> buildOperand) where TContext : ParserRuleContext
			{
				var expression = buildOperand(operands[0]);
				var operators = OperatorTexts(context);
				for (var i = 1; i < operands.Length; ++i)
					expression = new BinaryExpr(expression, NormalizeOperator(operators[i - 1]), buildOperand(operands[i]));
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

			private static bool IsBinaryOperator(int type) => type is
				GS2Parser.OR or GS2Parser.AND or GS2Parser.BAND or GS2Parser.BOR or GS2Parser.BXOR or GS2Parser.SHL or GS2Parser.SHR or
				GS2Parser.EQ or GS2Parser.NEQ or GS2Parser.NEQ_ALT or GS2Parser.LT or GS2Parser.LTE or GS2Parser.LTE_ALT or GS2Parser.GT or GS2Parser.GTE or GS2Parser.GTE_ALT or
				GS2Parser.CONCAT or GS2Parser.PLUS or GS2Parser.MINUS or GS2Parser.MUL or GS2Parser.DIV or GS2Parser.MOD or GS2Parser.POW;

			private static ParseFailure Failure(ParserRuleContext context) => new(context.Start.Line);
		}
	}

	private static class AntlrGs1Parser
	{
		public static ParseResult Parse(string code)
		{
			try
			{
				var input = CharStreams.fromString(code);
				var lexer = new GS1Lexer(input);
				var tokens = new CommonTokenStream(lexer);
				var parser = new GS1Parser(tokens);
				var listener = new ThrowingErrorListener();
				lexer.RemoveErrorListeners();
				parser.RemoveErrorListeners();
				lexer.AddErrorListener(listener);
				parser.AddErrorListener(listener);
				var program = parser.program();
				return parser.NumberOfSyntaxErrors == 0
					? new(new Builder(code).Build(program), null)
					: new(null, ErrorMessage(code, 1));
			}
			catch (ParseFailure ex)
			{
				return new(null, ErrorMessage(code, ex.Line));
			}
		}

		private sealed class Builder
		{
			public Builder(string code) => _ = code;

			public ProgramNode Build(GS1Parser.ProgramContext context)
			{
				List<ProgramItem> items = [];
				var gs1EventFlags = 0;
				foreach (var block in context.block())
				{
					if (SingleStatement(block)?.functionDefinition() is { } functionDefinition) items.Add(new FunctionItem(BuildFunction(functionDefinition)));
					else
					{
						var statement = new BlockStmt(BuildBlock(block));
						gs1EventFlags |= Gs1EventFlagScanner.Scan(statement);
						items.Add(new StatementItem(statement));
					}
				}
				return new([], [], items, gs1EventFlags);
			}

			private static GS1Parser.StatementContext? SingleStatement(GS1Parser.BlockContext context) =>
				context.statement().Length == 1 && context.GetChild(0) is GS1Parser.StatementContext ? context.statement(0) : null;

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

			private IfStmt BuildIf(GS1Parser.IfStatementContext context)
			{
				if (context is not GS1Parser.StatementIfContext ifStatement) throw Failure(context);
				var blocks = ifStatement.block();
				var hasElse = blocks.Length > 1;
				return new(BuildExpression(ifStatement.expression()), BuildBlock(blocks[0]), hasElse ? BuildBlock(blocks[1]) : [], hasElse);
			}

			private ForStmt BuildFor(GS1Parser.ForStatementContext context)
			{
				if (context is not GS1Parser.StatementForContext forStatement) throw Failure(context);
				var assignments = forStatement.assignmentStatement();
				var expressions = forStatement.expression();
				Expr? init = null;
				Expr? post = null;
				var expressionIndex = 0;
				if (assignments.Length > 0 && assignments[0].Start.TokenIndex < forStatement.END(0).Symbol.TokenIndex) init = BuildAssignmentStatement(assignments[0]);
				else if (expressions.Length > expressionIndex && expressions[expressionIndex].Start.TokenIndex < forStatement.END(0).Symbol.TokenIndex) init = BuildExpression(expressions[expressionIndex++]);
				var condition = expressions.Length > expressionIndex && expressions[expressionIndex].Start.TokenIndex < forStatement.END(1).Symbol.TokenIndex ? BuildExpression(expressions[expressionIndex++]) : new BoolExpr(true);
				if (assignments.Length > 0 && assignments[^1].Start.TokenIndex > forStatement.END(1).Symbol.TokenIndex) post = BuildAssignmentStatement(assignments[^1]);
				else if (expressions.Length > expressionIndex) post = BuildExpression(expressions[expressionIndex]);
				return new(init, condition, post, BuildBlock(forStatement.block()));
			}

			private WhileStmt BuildWhile(GS1Parser.WhileStatementContext context)
			{
				if (context is not GS1Parser.StatementWhileContext whileStatement) throw Failure(context);
				return new(BuildExpression(whileStatement.expression()), BuildBlock(whileStatement.block()));
			}

			private Stmt BuildWith(GS1Parser.WithStatementContext context)
			{
				if (context is not GS1Parser.StatementWithContext withStatement) throw Failure(context);
				return new WithStmt(BuildExpression(withStatement.expression()), BuildBlock(withStatement.block()));
			}

			private static Stmt BuildFlow(GS1Parser.FlowStatementContext context) => context switch
			{
				GS1Parser.FlowReturnContext => new ReturnStmt(new NumberExpr("0")),
				GS1Parser.FlowBreakContext => new BreakStmt(),
				GS1Parser.FlowContinueContext => new ContinueStmt(),
				_ => throw Failure(context),
			};

			private BinaryExpr BuildAssignmentStatement(GS1Parser.AssignmentStatementContext context)
			{
				if (context is not GS1Parser.StatementAssignmentContext assignment) throw Failure(context);
				return new(BuildIdentifierAccess(assignment.identifier_access()), NormalizeOperator(assignment.assignment_operator().GetText()), BuildExpression(assignment.expression()));
			}

			private ExprStmt BuildCommand(GS1Parser.BuiltinCommandStatementContext context)
			{
				if (context is not GS1Parser.StatementBuiltInCommandContext command) throw Failure(context);
				var name = command.COMMAND().GetText().Trim();
				var args = command.builtInCommandExpression().Select(BuildBuiltInCommandExpression).ToList();
				if (name == "set" && args.Count > 0) return new(new BinaryExpr(args[0], "=", new BoolExpr(true)));
				if (name == "unset" && args.Count > 0) return new(new BinaryExpr(args[0], "=", new BoolExpr(false)));
				return new(new CallExpr(name, args));
			}

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

			private Expr BuildExpression(GS1Parser.ExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionTernaryContext ternary) throw Failure(context);
				var expression = BuildLogicalOr(ternary.logicalOrExpression());
				var branches = ternary.expression();
				for (var i = 0; i + 1 < branches.Length; i += 2)
					expression = new TernaryExpr(expression, BuildExpression(branches[i]), BuildExpression(branches[i + 1]));
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
				return BuildBinaryChain(relational, relational.additiveExpression(), BuildAdditive);
			}

			private Expr BuildAdditive(GS1Parser.AdditiveExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionAdditiveContext additive) throw Failure(context);
				return BuildBinaryChildren(additive, child => child switch
				{
					GS1Parser.MultiplicativeExpressionContext multiplicative => BuildMultiplicative(multiplicative),
					ITerminalNode { Symbol.Type: GS1Parser.LITERAL } literal => BuildLiteral(literal.GetText()),
					_ => null,
				});
			}

			private Expr BuildMultiplicative(GS1Parser.MultiplicativeExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionMultiplicativeContext multiplicative) throw Failure(context);
				return BuildBinaryChildren(multiplicative, child => child switch
				{
					GS1Parser.InExpressionContext inExpression => BuildIn(inExpression),
					ITerminalNode { Symbol.Type: GS1Parser.LITERAL } literal => BuildLiteral(literal.GetText()),
					_ => null,
				});
			}

			private Expr BuildIn(GS1Parser.InExpressionContext context)
			{
				if (context is not GS1Parser.ExpressionInContext inExpression) throw Failure(context);
				var left = BuildExponentiation(inExpression.exponentiationExpression(0));
				if (inExpression.OP_IN() == null) return left;
				if (inExpression.range_literal() is { } rangeLiteral)
				{
					if (rangeLiteral is not GS1Parser.RangeLiteralContext range) throw Failure(rangeLiteral);
					var expressions = range.expression();
					return new InExpr(left, BuildExpression(expressions[0]), BuildExpression(expressions[1]));
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
				var op = unary.GetChild(0).GetText();
				var expression = BuildUnary(unary.unaryExpression());
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

			private CallExpr BuildBuiltInFunction(GS1Parser.Builtin_functionContext context)
			{
				if (context is not GS1Parser.BuiltInFunctionCallContext call) throw Failure(context);
				return new(call.FUNCTION().GetText(), call.expression().Select(BuildExpression).ToList());
			}

			private Expr BuildIdentifierAccess(GS1Parser.Identifier_accessContext context)
			{
				if (context is not GS1Parser.IdentifierAccessContext access) throw Failure(context);
				var values = access.identifier_value();
				var (name, indices) = BuildIdentifierValue(values[0]);
				var expression = BuildDottedIdentifier(name, indices);
				for (var i = 1; i < values.Length; ++i)
				{
					(name, indices) = BuildIdentifierValue(values[i]);
					expression = BuildDottedMember(expression, name, indices);
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
				var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length == 0) return ApplyIndices(new IdentifierExpr(name), indices);
				Expr expression = new IdentifierExpr(parts[0]);
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

			private static Expr ApplyIndices(Expr expression, List<Expr> indices) => indices.Count switch
			{
				0 => expression,
				1 => new ArrayIndexExpr(expression, indices[0]),
				_ => new MultiArrayIndexExpr(expression, indices),
			};

			private ArrayLiteralExpr BuildArrayLiteral(GS1Parser.Array_literalContext context)
			{
				if (context is not GS1Parser.ArrayLiteralContext arrayLiteral) throw Failure(context);
				List<Expr> values = [];
				var expectingValue = true;
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

			private static Expr BuildSpecialLiteral(GS1Parser.Special_literalContext context) => context switch
			{
				GS1Parser.DirectionLiteralContext => DirectionExpr(context.GetText()),
				_ => new StringExpr(context.GetText()),
			};

			private static Expr BuildCompoundString(GS1Parser.Compound_stringContext context) => new StringExpr(context.GetText());

			private static Expr BuildLiteral(string text) => text switch
			{
				"true" => new BoolExpr(true),
				"false" => new BoolExpr(false),
				"allfeatures" or "allstats" => new IdentifierExpr(text),
				_ => Number(text),
			};

			private static NumberExpr Number(string text)
			{
				if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
					return new(Convert.ToInt32(text[2..], 16).ToString(CultureInfo.InvariantCulture));
				return new(text);
			}

			private static Expr DirectionExpr(string text) => text switch
			{
				"up" => new NumberExpr("0"),
				"left" => new NumberExpr("1"),
				"down" => new NumberExpr("2"),
				"right" => new NumberExpr("3"),
				_ => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? new NumberExpr((value % 4).ToString(CultureInfo.InvariantCulture)) : new StringExpr(text),
			};

			private static Expr BuildBinaryChain<TContext>(ParserRuleContext context, TContext[] operands, Func<TContext, Expr> buildOperand) where TContext : ParserRuleContext
			{
				var expression = buildOperand(operands[0]);
				var operators = OperatorTexts(context);
				for (var i = 1; i < operands.Length; ++i)
					expression = new BinaryExpr(expression, NormalizeOperator(operators[i - 1]), buildOperand(operands[i]));
				return expression;
			}

			private static Expr BuildBinaryChildren(ParserRuleContext context, Func<IParseTree, Expr?> buildOperand)
			{
				Expr? expression = null;
				string? op = null;
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
					else expression = new BinaryExpr(expression, op ?? throw Failure(context), operand);
					op = null;
				}
				return expression ?? throw Failure(context);
			}

			private static List<string> OperatorTexts(ParserRuleContext context)
			{
				List<string> operators = [];
				for (var i = 0; i < context.ChildCount; ++i)
					if (context.GetChild(i) is ITerminalNode terminal && IsBinaryOperator(terminal.Symbol.Type))
						operators.Add(terminal.Symbol.Type == GS1Parser.OP_ASSIGN ? "==" : NormalizeOperator(terminal.GetText()));
				return operators;
			}

			private static bool IsBinaryOperator(int type) => type is
				GS1Parser.OP_LOGICALOR or GS1Parser.OP_LOGICALAND or GS1Parser.OP_EQUAL or GS1Parser.OP_ASSIGN or GS1Parser.OP_NOTEQ or
				GS1Parser.OP_LESS or GS1Parser.OP_GREAT or GS1Parser.OP_LESS_EQ or GS1Parser.OP_GREAT_EQ or GS1Parser.OP_ADD or GS1Parser.OP_SUB or
				GS1Parser.OP_MUL or GS1Parser.OP_DIV or GS1Parser.OP_MOD or GS1Parser.OP_POW;

			private static ParseFailure Failure(ParserRuleContext context) => new(context.Start.Line);
		}
	}

	private static class Gs1EventFlagScanner
	{
		private static readonly Dictionary<string, int> EventFlags = new(StringComparer.OrdinalIgnoreCase)
		{
			["playerenters"]       = 1 << 0,
			["playertouchsme"]     = 1 << 1,
			["playertouchesme"]    = 1 << 1,
			["playertouchsother"]  = 1 << 2,
			["playertouchesother"] = 1 << 2,
			["playerchats"]        = 1 << 3,
			["playerhurt"]         = 1 << 4,
			["playerhurted"]       = 1 << 4,
			["playerdies"]         = 1 << 5,
			["playerlaysitem"]     = 1 << 6,
			["playerendsreading"]  = 1 << 7,
			["compusdied"]         = 1 << 8,
			["emoticon"]           = 1 << 9,
			["mousedown"]          = 1 << 10,
			["mouseup"]            = 1 << 11,
			["mousewheel"]         = 1 << 12,
			["exploded"]           = 1 << 13,
			["wasshot"]            = 1 << 14,
			["wasshooted"]         = 1 << 14,
			["waspelt"]            = 1 << 15,
			["keypressed"]         = 1 << 16,
			["actionprojectile2"]  = 1 << 17,
			["playerleaves"]       = 1 << 21,
			["washit"]             = 1 << 22,
			["shapetrigger"]       = 1 << 25,
			["playanimation"]      = 1 << 26,
			["pkzonechanges"]      = 1 << 27,
		};

		public static int Scan(Stmt statement) => ScanStatement(statement);

		private static int ScanStatements(List<Stmt> statements)
		{
			var flags = 0;
			foreach (var statement in statements) flags |= ScanStatement(statement);
			return flags;
		}

		private static int ScanStatement(Stmt statement) => statement switch
		{
			ExprStmt exprStmt => ScanExpression(exprStmt.Expression),
			InlineStmt inline => ScanStatement(inline.Statement),
			BlockStmt block => ScanStatements(block.Body),
			ReturnStmt returnStmt => ScanExpression(returnStmt.Expression),
			IfStmt ifStmt => ScanExpression(ifStmt.Condition) | ScanStatements(ifStmt.ThenBody) | ScanStatements(ifStmt.ElseBody),
			ForStmt forStmt => ScanExpression(forStmt.Init) | ScanExpression(forStmt.Condition) | ScanExpression(forStmt.Post) | ScanStatements(forStmt.Body),
			ForEachStmt forEachStmt => ScanExpression(forEachStmt.Name) | ScanExpression(forEachStmt.Source) | ScanStatements(forEachStmt.Body),
			WhileStmt whileStmt => ScanExpression(whileStmt.Condition) | ScanStatements(whileStmt.Body),
			WithStmt withStmt => ScanExpression(withStmt.Target) | ScanStatements(withStmt.Body),
			SwitchStmt switchStmt => ScanSwitch(switchStmt),
			NewStmt newStmt => ScanExpressions(newStmt.Args) | ScanStatements(newStmt.Body),
			_ => 0,
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
			var flags = 0;
			foreach (var expression in expressions) flags |= ScanExpression(expression);
			return flags;
		}

		private static int ScanExpression(Expr? expression) => expression switch
		{
			null => 0,
			BinaryExpr { Op: "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "^=" or "@=" or "<<=" or ">>=" } binary => ScanExpression(binary.Right),
			BinaryExpr binary => ScanExpression(binary.Left) | ScanExpression(binary.Right),
			InExpr inExpr => ScanExpression(inExpr.Expression) | ScanExpression(inExpr.Lower) | ScanExpression(inExpr.Upper),
			TernaryExpr ternary => ScanExpression(ternary.Condition) | ScanExpression(ternary.WhenTrue) | ScanExpression(ternary.WhenFalse),
			UnaryExpr unary => ScanExpression(unary.Expression),
			StringCastExpr cast => ScanExpression(cast.Expression),
			CastExpr cast => ScanExpression(cast.Expression),
			MemberExpr member => ScanExpression(member.Object),
			DynamicMemberExpr member => ScanExpression(member.Object) | ScanExpression(member.Name),
			DynamicVarExpr dynamicVar => ScanExpression(dynamicVar.Name),
			ArrayIndexExpr arrayIndex => ScanExpression(arrayIndex.Target) | ScanExpression(arrayIndex.Index),
			MultiArrayIndexExpr arrayIndex => ScanExpression(arrayIndex.Target) | ScanExpressions(arrayIndex.Indices),
			IdentifierExpr identifier => EventFlags.GetValueOrDefault(identifier.Name),
			CallExpr call => ScanExpressions(call.Args),
			ChainedCallExpr call => ScanExpression(call.Call) | ScanExpressions(call.Args),
			MethodCallExpr call => ScanExpression(call.Object) | ScanExpressions(call.Args),
			DynamicMethodCallExpr call => ScanExpression(call.Object) | ScanExpression(call.Name) | ScanExpressions(call.Args),
			NewObjectExpr newObject => ScanExpressions(newObject.Args),
			NewArrayExpr newArray => ScanExpressions(newArray.Dimensions),
			_ => 0,
		};
	}

	private sealed class Emitter(
		BytecodeWriter bytecode,
		Dictionary<string, Expr> constants,
		Dictionary<string, Dictionary<string, int>> enums
	)
	{
		private readonly Stack<List<int>>                            _breakPatches            = new();
		private readonly Stack<List<int>>                            _continuePatches         = new();
		private readonly Stack<List<int>>                            _conditionFailPatches    = new();
		private readonly Stack<List<int>>                            _conditionSuccessPatches = new();
		private readonly Dictionary<string, string>                  _negativeFloatText       = new(StringComparer.Ordinal);
		private readonly Stack<HashSet<string>>                      _tempAliases             = new();
		private          int                                         _newObjectCount;
		private          bool                                        _dynamicMethodName;

		public void EmitFunction(FunctionNode node, bool patchToFinal = true)
		{
			bytecode.Emit(Op.SetIndex);
			var jmpLoc = bytecode.EmitNumberOperandPlaceholder();
			var functionName = node.Public ? "public." : "";
			if (!string.IsNullOrEmpty(node.ObjectName)) functionName += node.ObjectName + ".";
			functionName += node.Name;
			if (node.ObjectName == "universe") functionName = (node.Public ? "public." : "") + node.Name + "," + functionName;
			var addedFunction = bytecode.AddFunction(functionName, bytecode.OpIndex, 0);
			_tempAliases.Push(CollectTempAliases(node));
			bytecode.Emit(Op.TypeArray);
			for (var i = node.Args.Count - 1; i >= 0; --i) Emit(node.Args[i]);
			bytecode.Emit(Op.FuncParamsEnd);
			bytecode.Emit(Op.Jmp);
			if (node.Body.Exists(ContainsCall)) bytecode.Emit(Op.CmdCall);
			foreach (var statement in node.Body) EmitStatement(statement);
			_tempAliases.Pop();
			var lastStatement = LastMeaningfulStatement(node.Body);
			if (node.Name != "getPreviewSpriteCount" && (lastStatement == null || !DefinitelyReturns(lastStatement)))
			{
				bytecode.Emit(Op.TypeNumber);
				bytecode.EmitDynamicNumber(0);
				bytecode.Emit(Op.Ret);
			}
			if (patchToFinal)
			{
				if (addedFunction) bytecode.AddPrejumpPatch(jmpLoc);
			}
			else bytecode.PatchShort(jmpLoc, bytecode.OpIndex);
		}

		public void EmitTopLevel(List<Stmt> statements)
		{
			foreach (var statement in statements) EmitStatement(statement);
		}

		private static bool DefinitelyReturns(Stmt statement) => statement switch
		{
			ReturnStmt => true,
			InlineStmt inlineStatement => DefinitelyReturns(inlineStatement.Statement),
			BlockStmt blockStatement => LastMeaningfulStatement(blockStatement.Body) is { } lastStatement && DefinitelyReturns(lastStatement),
			IfStmt ifStatement => ifStatement.HasElse && BodyDefinitelyReturns(ifStatement.ThenBody) && BodyDefinitelyReturns(ifStatement.ElseBody),
			_ => false,
		};

		private static bool BodyDefinitelyReturns(List<Stmt> body) => LastMeaningfulStatement(body) is { } lastStatement && DefinitelyReturns(lastStatement);

		private static Stmt? LastMeaningfulStatement(List<Stmt> body)
		{
			for (var i = body.Count - 1; i >= 0; --i)
				if (body[i] is not BlockStmt { Body.Count: 0 }) return body[i];
			return null;
		}

		private void Emit(Expr expr) => Emit(expr, false, true, 0);

		private void EmitUnusedExpression(Expr expr)
		{
			var preservePostfixValue = expr is not UnaryExpr { Op: "++" or "--" };
			Emit(expr, false, true, 0, false, false, preservePostfixValue);
		}

		public void EmitStatement(Stmt statement) => EmitStatement(statement, true);

		private void EmitStatement(Stmt statement, bool discardCallReturn)
		{
			if (statement is ExprStmt exprStatement)
			{
				if (!discardCallReturn) Emit(exprStatement.Expression);
				else EmitUnusedExpression(exprStatement.Expression);
				if (!discardCallReturn) { }
				else if (exprStatement.Expression is CallExpr call && NonReturningBuiltInCalls.Contains(call.Name)) { }
				else if (exprStatement.Expression is MethodCallExpr methodCall && NonReturningMethodCalls.Contains(methodCall.Name)) { }
				else if (exprStatement.Expression is CallExpr or MethodCallExpr or DynamicMethodCallExpr) bytecode.Emit(Op.IndexDec);
			}
			else if (statement is ReturnStmt returnStatement)
			{
				Emit(returnStatement.Expression);
				bytecode.Emit(Op.Ret);
			}
			else if (statement is IfStmt ifStatement) EmitIf(ifStatement);
			else if (statement is ForStmt forStatement) EmitFor(forStatement);
			else if (statement is ForEachStmt forEachStatement) EmitForEach(forEachStatement);
			else if (statement is WhileStmt whileStatement) EmitWhile(whileStatement);
			else if (statement is WithStmt withStatement) EmitWith(withStatement);
			else if (statement is SwitchStmt switchStatement) EmitSwitch(switchStatement);
			else if (statement is BreakStmt) EmitBreak();
			else if (statement is ContinueStmt) EmitContinue();
			else if (statement is InlineStmt inlineStatement) EmitStatement(inlineStatement.Statement, false);
			else if (statement is BlockStmt blockStatement) foreach (var stmt in blockStatement.Body) EmitStatement(stmt);
			else if (statement is NewStmt newStatement) EmitNewStatement(newStatement);
		}

		private void EmitIf(IfStmt statement)
		{
			List<int> conditionFailPatches = [];
			List<int> conditionSuccessPatches = [];
			_conditionFailPatches.Push(conditionFailPatches);
			_conditionSuccessPatches.Push(conditionSuccessPatches);
			if (UsesInlineForCondition(statement.Condition)) Emit(statement.Condition);
			else Emit(statement.Condition, false, false, 0, true);
			_conditionSuccessPatches.Pop();
			_conditionFailPatches.Pop();
			if (!IsBooleanExpr(statement.Condition) && NeedsNumericConversion(statement.Condition)) bytecode.Emit(Op.ConvToFloat);
			foreach (var patch in conditionSuccessPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			bytecode.Emit(Op.If);
			var failLoc = bytecode.EmitNumberOperandPlaceholder();
			foreach (var stmt in statement.ThenBody) EmitStatement(stmt);
			var failTarget = bytecode.OpIndex + (statement.HasElse ? 1 : 0);
			bytecode.PatchShort(failLoc, failTarget);
			foreach (var patch in conditionFailPatches) bytecode.PatchShort(patch, failTarget);
			if (statement.HasElse)
			{
				bytecode.Emit(Op.SetIndex);
				var exitLoc = bytecode.EmitNumberOperandPlaceholder();
				foreach (var stmt in statement.ElseBody) EmitStatement(stmt);
				bytecode.PatchShort(exitLoc, bytecode.OpIndex);
			}
		}

		private void EmitFor(ForStmt statement)
		{
			if (statement.Init != null) EmitUnusedExpression(statement.Init);
			var start = bytecode.OpIndex;
			List<int> conditionFailPatches = [];
			List<int> conditionSuccessPatches = [];
			_conditionFailPatches.Push(conditionFailPatches);
			_conditionSuccessPatches.Push(conditionSuccessPatches);
			if (UsesInlineForCondition(statement.Condition)) Emit(statement.Condition);
			else Emit(statement.Condition, false, false, 0, true);
			_conditionSuccessPatches.Pop();
			_conditionFailPatches.Pop();
			if (!IsBooleanExpr(statement.Condition) && NeedsNumericConversion(statement.Condition)) bytecode.Emit(Op.ConvToFloat);
			foreach (var patch in conditionSuccessPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			bytecode.Emit(Op.If);
			var breakLoc = bytecode.EmitNumberOperandPlaceholder();
			List<int> breakPatches = [];
			_breakPatches.Push(breakPatches);
			List<int> continuePatches = [];
			_continuePatches.Push(continuePatches);
			bytecode.Emit(Op.CmdCall);
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			foreach (var patch in continuePatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			if (statement.Post != null) EmitUnusedExpression(statement.Post);
			bytecode.Emit(Op.SetIndex);
			bytecode.EmitDynamicNumber(start);
			_continuePatches.Pop();
			_breakPatches.Pop();
			bytecode.PatchShort(breakLoc, bytecode.OpIndex);
			foreach (var patch in breakPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			foreach (var patch in conditionFailPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
		}

		private void EmitForEach(ForEachStmt statement)
		{
			Emit(statement.Name);
			Emit(statement.Source);
			bytecode.Emit(Op.ConvToObject);
			bytecode.Emit(Op.TypeNumber);
			bytecode.EmitDynamicNumber(0);
			var start = bytecode.OpIndex;
			bytecode.Emit(Op.Foreach);
			var breakLoc = bytecode.EmitNumberOperandPlaceholder();
			List<int> breakPatches = [];
			_breakPatches.Push(breakPatches);
			List<int> continuePatches = [];
			_continuePatches.Push(continuePatches);
			bytecode.Emit(Op.CmdCall);
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			foreach (var patch in continuePatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			bytecode.Emit(Op.Inc);
			bytecode.Emit(Op.SetIndex);
			bytecode.EmitDynamicNumber(start);
			_continuePatches.Pop();
			_breakPatches.Pop();
			bytecode.PatchShort(breakLoc, bytecode.OpIndex);
			foreach (var patch in breakPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			bytecode.Emit(Op.IndexDec);
		}

		private void EmitWhile(WhileStmt statement)
		{
			var start = bytecode.OpIndex;
			List<int> conditionFailPatches = [];
			List<int> conditionSuccessPatches = [];
			_conditionFailPatches.Push(conditionFailPatches);
			_conditionSuccessPatches.Push(conditionSuccessPatches);
			Emit(statement.Condition, false, false, 0, true);
			_conditionSuccessPatches.Pop();
			_conditionFailPatches.Pop();
			if (!IsBooleanExpr(statement.Condition) && NeedsNumericConversion(statement.Condition)) bytecode.Emit(Op.ConvToFloat);
			foreach (var patch in conditionSuccessPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			List<int> breakPatches = [];
			_breakPatches.Push(breakPatches);
			List<int> continuePatches = [];
			_continuePatches.Push(continuePatches);
			bytecode.Emit(Op.If);
			var breakLoc = bytecode.EmitNumberOperandPlaceholder();
			breakPatches.Add(breakLoc);
			bytecode.Emit(Op.CmdCall);
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			foreach (var patch in continuePatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			bytecode.Emit(Op.SetIndex);
			bytecode.EmitNumberOperand(start);
			_continuePatches.Pop();
			_breakPatches.Pop();
			foreach (var patch in breakPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			foreach (var patch in conditionFailPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
		}

		private void EmitWith(WithStmt statement)
		{
			Emit(statement.Target);
			bytecode.Emit(Op.ConvToObject);
			bytecode.Emit(Op.With);
			var exitLoc = bytecode.EmitNumberOperandPlaceholder();
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			bytecode.Emit(Op.WithEnd);
			bytecode.PatchShort(exitLoc, bytecode.OpIndex);
		}

		private void EmitSwitch(SwitchStmt statement)
		{
			bytecode.Emit(Op.SetIndex);
			var caseTestLoc = bytecode.EmitNumberOperandPlaceholder();
			List<int> caseStarts = [];
			List<int> breakPatches = [];
			_breakPatches.Push(breakPatches);
			foreach (var switchCase in statement.Cases)
			{
				var start = bytecode.OpIndex;
				foreach (var _ in switchCase.Labels) caseStarts.Add(start);
				List<int> continuePatches = [];
				_continuePatches.Push(continuePatches);
				foreach (var stmt in switchCase.Body) EmitStatement(stmt);
				_continuePatches.Pop();
				foreach (var patch in continuePatches) bytecode.PatchShort(patch, start);
			}
			_breakPatches.Pop();
			bytecode.PatchShort(caseTestLoc, bytecode.OpIndex);
			Emit(statement.Expression);
			var caseIndex = 0;
			foreach (var switchCase in statement.Cases)
			foreach (var label in switchCase.Labels)
			{
				if (label != null)
				{
					bytecode.Emit(Op.CopyLastOp);
					Emit(label);
					bytecode.Emit(Op.Eq);
					bytecode.Emit(Op.SetIndexTrue);
				}
				else bytecode.Emit(Op.SetIndex);
				bytecode.EmitDynamicNumber(caseStarts[caseIndex++]);
			}
			foreach (var patch in breakPatches) bytecode.PatchShort(patch, bytecode.OpIndex);
			bytecode.Emit(Op.IndexDec);
		}

		private void EmitBreak()
		{
			if (_breakPatches.Count == 0) return;
			bytecode.Emit(Op.SetIndex);
			_breakPatches.Peek().Add(bytecode.EmitNumberOperandPlaceholder());
		}

		private void EmitContinue()
		{
			if (_continuePatches.Count == 0) return;
			bytecode.Emit(Op.SetIndex);
			_continuePatches.Peek().Add(bytecode.EmitNumberOperandPlaceholder());
		}

		private void EmitNewStatement(NewStmt statement)
		{
			foreach (var arg in statement.Args) Emit(arg);
			bytecode.Emit(Op.InlineNew);
			bytecode.Emit(Op.CopyLastOp);
			bytecode.Emit(Op.CopyLastOp);
			bytecode.Emit(Op.CopyLastOp);
			bytecode.Emit(Op.TypeString);
			bytecode.EmitDynamicStringIndex(bytecode.GetString(statement.TypeName));
			bytecode.Emit(Op.ConvToString);
			bytecode.Emit(Op.NewObject);
			bytecode.Emit(Op.Assign);
			bytecode.Emit(Op.ConvToObject);
			bytecode.Emit(Op.With);
			var withLoc = bytecode.EmitNumberOperandPlaceholder();
			var previousCount = _newObjectCount++;
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			bytecode.Emit(Op.WithEnd);
			bytecode.PatchShort(withLoc, bytecode.OpIndex);
			for (var i = 0; i < _newObjectCount - previousCount; ++i)
			{
				bytecode.Emit(Op.TypeArray);
				bytecode.Emit(Op.SwapLastOps);
				bytecode.Emit(Op.TypeVar);
				bytecode.EmitDynamicStringIndex(bytecode.GetString("addcontrol"));
				bytecode.Emit(Op.Call);
				bytecode.Emit(Op.IndexDec);
			}
			_newObjectCount--;
		}

		private void Emit(Expr expr, bool copyAssignmentTarget, bool logicalInline, int suppressedLogicalPatchOffset, bool controlLogical = false, bool negatedLogical = false, bool preservePostfixValue = true, bool leftNegatedControlAnd = false)
		{
			switch (expr)
			{
				case BinaryExpr { Op: "=", Left: MultiArrayIndexExpr left, Right: var right }:
					EmitMultiArrayIndex(left, true);
					if (copyAssignmentTarget) bytecode.Emit(Op.CopyLastOp);
					if (right is BinaryExpr { Op: "=" }) Emit(right, true, true, 0);
					else Emit(right);
					bytecode.Emit(Op.ArrayMultiDimAssign);
					break;
				case BinaryExpr { Op: "=", Left: ArrayIndexExpr left, Right: var right }:
					EmitArrayIndex(left, true);
					if (copyAssignmentTarget) bytecode.Emit(Op.CopyLastOp);
					if (right is BinaryExpr { Op: "=" }) Emit(right, true, true, 0);
					else Emit(right);
					bytecode.Emit(Op.ArrayAssign);
					break;
				case BinaryExpr { Op: "=", Left: var left, Right: var right }:
					Emit(left);
					if (copyAssignmentTarget) bytecode.Emit(Op.CopyLastOp);
					if (right is BinaryExpr { Op: "=" }) Emit(right, true, true, 0);
					else Emit(right);
					bytecode.Emit(Op.Assign);
					break;
				case BinaryExpr { Op: var op, Left: MultiArrayIndexExpr left, Right: var right } when IsCompoundAssign(op):
					EmitMultiArrayIndex(left, true);
					bytecode.Emit(Op.CopyLastOp);
					bytecode.Emit(op == "@=" ? Op.ConvToString : Op.ConvToFloat);
					Emit(right);
					if (op == "@=" && NeedsStringConversion(right)) bytecode.Emit(Op.ConvToString);
					else if (op != "@=" && NeedsNumericConversion(right)) bytecode.Emit(Op.ConvToFloat);
					bytecode.Emit(CompoundOpcode(op));
					bytecode.Emit(Op.ArrayMultiDimAssign);
					break;
				case BinaryExpr { Op: var op, Left: ArrayIndexExpr left, Right: var right } when IsCompoundAssign(op):
					EmitArrayIndex(left, true);
					bytecode.Emit(Op.CopyLastOp);
					bytecode.Emit(op == "@=" ? Op.ConvToString : Op.ConvToFloat);
					Emit(right);
					if (op == "@=" && NeedsStringConversion(right)) bytecode.Emit(Op.ConvToString);
					else if (op != "@=" && NeedsNumericConversion(right)) bytecode.Emit(Op.ConvToFloat);
					bytecode.Emit(CompoundOpcode(op));
					bytecode.Emit(Op.ArrayAssign);
					break;
				case BinaryExpr { Op: var op, Left: var left, Right: var right } when IsCompoundAssign(op):
					Emit(left);
					bytecode.Emit(Op.CopyLastOp);
					bytecode.Emit(op == "@=" ? Op.ConvToString : Op.ConvToFloat);
					Emit(right);
					if (op == "@=" && NeedsStringConversion(right)) bytecode.Emit(Op.ConvToString);
					else if (op != "@=" && NeedsNumericConversion(right)) bytecode.Emit(Op.ConvToFloat);
					bytecode.Emit(CompoundOpcode(op));
					bytecode.Emit(Op.Assign);
					break;
				case BinaryExpr { Op: "&&" or "||" } logical:
					if (!controlLogical && !negatedLogical && !leftNegatedControlAnd && HasNestedLogical(logical, logical.Op))
					{
						EmitLogicalChain(logical, logicalInline, suppressedLogicalPatchOffset);
						break;
					}
					List<int>? localSuccessPatches = null;
					List<int>? localFailPatches = null;
					var localSuccessStart = 0;
					var localFailStart = 0;
					if (logical.Op == "&&" && controlLogical && _conditionSuccessPatches.Count > 0)
					{
						localSuccessPatches = _conditionSuccessPatches.Peek();
						localSuccessStart = localSuccessPatches.Count;
					}
					if (logical.Op == "||" && controlLogical && _conditionFailPatches.Count > 0)
					{
						localFailPatches = _conditionFailPatches.Peek();
						localFailStart = localFailPatches.Count;
					}
					var passNegatedControlAnd = controlLogical && logical.Left is UnaryExpr { Op: "!", Expression: BinaryExpr { Op: "&&" } };
					var leftPatchOffset = logical.Left is BinaryExpr { Op: "&&" or "||" } ? (controlLogical || negatedLogical) && logical.Op == "||" ? 6 : logical.Op == "&&" && logical.Left is BinaryExpr { Op: "||" } ? 0 : 1 : 0;
					Emit(logical.Left, false, false, leftPatchOffset, controlLogical, negatedLogical, true, passNegatedControlAnd);
					if (!IsBooleanExpr(logical.Left)) bytecode.Emit(Op.ConvToFloat);
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
					bytecode.Emit(logical.Op == "&&" && controlLogical ? Op.If : logical.Op == "&&" ? Op.And : Op.Or);
					var loc = bytecode.EmitNumberOperandPlaceholder();
					Emit(logical.Right, false, false, 0, controlLogical, negatedLogical);
					if (!IsBooleanExpr(logical.Right)) bytecode.Emit(Op.ConvToFloat);
					if (logicalInline) bytecode.Emit(Op.InlineConditional);
					if (logical.Op == "&&" && controlLogical && _conditionFailPatches.Count > 0) _conditionFailPatches.Peek().Add(loc);
					else if (logical.Op == "||" && controlLogical && _conditionSuccessPatches.Count > 0) _conditionSuccessPatches.Peek().Add(loc);
					else bytecode.PatchShort(loc, bytecode.OpIndex - (logicalInline ? 1 : 0) + (!logicalInline ? suppressedLogicalPatchOffset : 0));
					break;
				case TernaryExpr ternary:
					EmitTernary(ternary);
					break;
				case BinaryExpr { Op: "@", Left: var left, Right: var right } when _dynamicMethodName:
					Emit(left);
					if (left is StringExpr || NeedsStringConversion(left)) bytecode.Emit(Op.ConvToString);
					Emit(right);
					if (right is not StringExpr) bytecode.Emit(Op.ConvToString);
					bytecode.Emit(Op.Join);
					break;
				case BinaryExpr { Op: " " or "\n" or "\t", Left: var left, Right: var right } spacedConcat:
					Emit(left);
					if (NeedsStringConversion(left)) bytecode.Emit(Op.ConvToString);
					bytecode.Emit(Op.TypeString);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(spacedConcat.Op));
					bytecode.Emit(Op.Join);
					Emit(right);
					if (NeedsStringConversion(right)) bytecode.Emit(Op.ConvToString);
					bytecode.Emit(Op.Join);
					break;
				case InExpr inExpr:
					Emit(inExpr.Expression);
					Emit(inExpr.Lower);
					if (inExpr.Upper != null)
					{
						if (!IsNumberExpr(inExpr.Lower)) bytecode.Emit(Op.ConvToFloat);
						Emit(inExpr.Upper);
						if (!IsNumberExpr(inExpr.Upper)) bytecode.Emit(Op.ConvToFloat);
						bytecode.Emit(Op.InRange);
					}
					else
					{
						bytecode.Emit(Op.ConvToObject);
						bytecode.Emit(Op.InObj);
					}
					break;
				case BinaryExpr { Op: "*", Left: UnaryExpr { Op: "-", Expression: var negatedLeft }, Right: var right }:
					Emit(negatedLeft);
					if (NeedsNumericConversion(negatedLeft)) bytecode.Emit(Op.ConvToFloat);
					Emit(right);
					if (NeedsNumericConversion(right)) bytecode.Emit(Op.ConvToFloat);
					bytecode.Emit(Op.Mul);
					bytecode.Emit(Op.UnarySub);
					break;
				case BinaryExpr { Op: "==" or "!=", Left: StringCastExpr { Expression: MemberExpr left }, Right: StringCastExpr { Expression: MemberExpr right } } castCompare:
					Emit(left);
					Emit(right);
					bytecode.Emit(Op.ConvToString);
					bytecode.Emit(BinaryOpcode(castCompare.Op));
					bytecode.Emit(Op.ConvToString);
					break;
				case BinaryExpr { Op: "==" or "!=", Left: StringCastExpr { Expression: MemberExpr left }, Right: StringCastExpr { Expression: ArrayLiteralExpr right } } arrayCastCompare:
					Emit(left);
					Emit(new StringCastExpr(right));
					bytecode.Emit(BinaryOpcode(arrayCastCompare.Op));
					bytecode.Emit(Op.ConvToString);
					break;
				case BinaryExpr { Op: "==" or "!=", Left: StringCastExpr { Expression: ArrayLiteralExpr left }, Right: StringCastExpr { Expression: ArrayLiteralExpr right } } arrayPairCastCompare:
					Emit(left);
					Emit(new StringCastExpr(right));
					bytecode.Emit(BinaryOpcode(arrayPairCastCompare.Op));
					bytecode.Emit(Op.ConvToString);
					break;
				case BinaryExpr binary:
					Emit(binary.Left);
					if (binary.Op == "@" && NeedsStringConversion(binary.Left)) bytecode.Emit(Op.ConvToString);
					if ((IsNumericOp(binary.Op) || IsComparisonOp(binary.Op)) && NeedsNumericConversion(binary.Left)) bytecode.Emit(Op.ConvToFloat);
					if (IsComparisonOp(binary.Op) && binary.Left is UnaryExpr { Op: "++" or "--", Postfix: true }) bytecode.Emit(Op.ConvToFloat);
					if (binary.Op == "%" && binary.Left is BinaryExpr { Op: "+=" or "-=" or "*=" or "/=" or "%=" }) bytecode.Emit(Op.ConvToFloat);
					if (binary.Op == "&" && binary.Left is not NumberExpr) bytecode.Emit(Op.ConvToFloat);
					if (binary.Op == "@" && binary.Left is StringCastExpr && binary.Right is TernaryExpr) bytecode.Emit(Op.ConvToString);
					if (IsNumericOp(binary.Op) && UsesPreviewSpriteTernary(binary.Left)) bytecode.Emit(Op.ConvToFloat);
					Emit(binary.Right);
					if ((IsNumericOp(binary.Op) || IsComparisonOp(binary.Op)) && NeedsNumericConversion(binary.Right)) bytecode.Emit(Op.ConvToFloat);
					if (IsNumericOp(binary.Op) && UsesSignTernary(binary.Right)) bytecode.Emit(Op.ConvToFloat);
					if (binary.Op == "@" && (_dynamicMethodName || NeedsStringConversion(binary.Right))) bytecode.Emit(Op.ConvToString);
					bytecode.Emit(BinaryOpcode(binary.Op));
					break;
				case MemberExpr member:
					Emit(member.Object);
					if (NeedsObjectConversion(member.Object)) bytecode.Emit(Op.ConvToObject);
					bytecode.Emit(Op.TypeVar);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(member.Name));
					bytecode.Emit(Op.MemberAccess);
					break;
				case DynamicMemberExpr member:
					Emit(member.Object);
					if (NeedsObjectConversion(member.Object)) bytecode.Emit(Op.ConvToObject);
					if (member.Name is StringCastExpr { Expression: IdentifierExpr or MemberExpr { Object: IdentifierExpr { Name: "temp" } } } nameCast)
					{
						Emit(nameCast.Expression);
						if (NeedsNumericConversion(nameCast.Expression)) bytecode.Emit(Op.ConvToFloat);
						bytecode.Emit(Op.Add);
					}
					else
					{
						Emit(member.Name);
						if (NeedsStringConversion(member.Name)) bytecode.Emit(Op.ConvToString);
					}
					bytecode.Emit(Op.MemberAccess);
					break;
				case StringCastExpr { Expression: IdentifierExpr id } when _tempAliases.TryPeek(out var tempAliases) && tempAliases.Contains(id.Name):
					Emit(new MemberExpr(new IdentifierExpr("temp"), id.Name));
					bytecode.Emit(Op.ConvToString);
					break;
				case StringCastExpr cast:
					Emit(cast.Expression);
					bytecode.Emit(Op.ConvToString);
					if (ExpressionTypeOf(cast.Expression) == ExprType.Array) bytecode.Emit(Op.MemberAccess);
					break;
				case ArrayIndexExpr index:
					EmitArrayIndex(index, false);
					break;
				case MultiArrayIndexExpr index:
					EmitMultiArrayIndex(index, false);
					break;
				case DynamicVarExpr dynamicVar:
					Emit(new CallExpr("makevar", [dynamicVar.Name]));
					break;
				case IdentifierExpr id when constants.TryGetValue(id.Name, out var constant):
					Emit(constant);
					break;
				case IdentifierExpr { Name: "this" }:
					bytecode.Emit(Op.This);
					break;
				case IdentifierExpr { Name: "temp" }:
					bytecode.Emit(Op.Temp);
					break;
				case IdentifierExpr { Name: "thiso" }:
					bytecode.Emit(Op.Thiso);
					break;
				case IdentifierExpr { Name: "player" }:
					bytecode.Emit(Op.Player);
					break;
				case IdentifierExpr { Name: "playero" }:
					bytecode.Emit(Op.Playero);
					break;
				case IdentifierExpr { Name: "level" }:
					bytecode.Emit(Op.Level);
					break;
				case IdentifierExpr { Name: "pi" }:
					bytecode.Emit(Op.Pi);
					break;
				case IdentifierExpr id:
					bytecode.Emit(Op.TypeVar);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(id.Name));
					break;
				case EnumExpr enumExpr:
					bytecode.Emit(Op.TypeNumber);
					bytecode.EmitDynamicNumber(enums.TryGetValue(enumExpr.EnumName, out var members) && members.TryGetValue(enumExpr.MemberName, out var value) ? value : 0);
					break;
				case NumberExpr number:
					bytecode.Emit(Op.TypeNumber);
					if (number.Text.Contains('.', StringComparison.Ordinal)) bytecode.EmitDoubleNumber(CurrentFloatText(number.Text));
					else bytecode.EmitDynamicNumber(int.Parse(number.Text, CultureInfo.InvariantCulture));
					break;
				case StringExpr str:
					bytecode.Emit(Op.TypeString);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(str.Value));
					break;
				case BoolExpr boolean:
					bytecode.Emit(boolean.Value ? Op.TypeTrue : Op.TypeFalse);
					break;
				case NullExpr:
					bytecode.Emit(Op.TypeNull);
					break;
				case UnaryExpr { Op: "-", Expression: NumberExpr number }:
					bytecode.Emit(Op.TypeNumber);
					if (number.Text.Contains('.', StringComparison.Ordinal)) bytecode.EmitDoubleNumber(NextNegativeFloatText(number.Text));
					else bytecode.EmitDynamicNumber(-int.Parse(number.Text, CultureInfo.InvariantCulture));
					break;
				case UnaryExpr { Op: "-", Expression: var unaryValue }:
					Emit(unaryValue);
					bytecode.Emit(Op.ConvToFloat);
					bytecode.Emit(Op.UnarySub);
					break;
				case UnaryExpr { Op: "!", Expression: var notValue }:
					Emit(notValue, false, false, 0, leftNegatedControlAnd, true);
					if (!IsBooleanExpr(notValue)) bytecode.Emit(Op.ConvToFloat);
					bytecode.Emit(Op.Not);
					break;
				case UnaryExpr { Op: "~", Expression: var invertValue }:
					Emit(invertValue);
					if (!IsNumberExpr(invertValue)) bytecode.Emit(Op.ConvToFloat);
					bytecode.Emit(Op.BitInvert);
					break;
				case UnaryExpr { Op: "++", Expression: var incValue, Postfix: true }:
					Emit(incValue);
					if (preservePostfixValue)
					{
						bytecode.Emit(Op.CopyLastOp);
						bytecode.Emit(Op.ConvToFloat);
						bytecode.Emit(Op.SwapLastOps);
					}
					bytecode.Emit(Op.Inc);
					bytecode.Emit(Op.IndexDec);
					break;
				case UnaryExpr { Op: "--", Expression: var decValue, Postfix: true }:
					Emit(decValue);
					if (preservePostfixValue)
					{
						bytecode.Emit(Op.CopyLastOp);
						bytecode.Emit(Op.ConvToFloat);
						bytecode.Emit(Op.SwapLastOps);
					}
					bytecode.Emit(Op.Dec);
					bytecode.Emit(Op.IndexDec);
					break;
				case UnaryExpr { Op: "++", Expression: var incValue }:
					Emit(incValue);
					bytecode.Emit(Op.Inc);
					if (!preservePostfixValue) bytecode.Emit(Op.IndexDec);
					break;
				case UnaryExpr { Op: "--", Expression: var decValue }:
					Emit(decValue);
					bytecode.Emit(Op.Dec);
					if (!preservePostfixValue) bytecode.Emit(Op.IndexDec);
					break;
				case CastExpr { Type: "int", Expression: var castValue }:
					Emit(castValue);
					if (!IsNumberExpr(castValue)) bytecode.Emit(Op.ConvToFloat);
					bytecode.Emit(Op.Int);
					break;
				case CastExpr { Type: "float", Expression: var castValue }:
					Emit(castValue);
					if (!IsNumberExpr(castValue)) bytecode.Emit(Op.ConvToFloat);
					break;
				case CastExpr { Type: "_", Expression: var castValue }:
					Emit(castValue);
					if (castValue is not StringExpr) bytecode.Emit(Op.ConvToString);
					bytecode.Emit(Op.Translate);
					break;
				case CallExpr call when BuiltInCalls.TryGetValue(call.Name, out var builtIn):
					for (var i = call.Args.Count - 1; i >= 0; --i)
					{
						Emit(call.Args[i]);
						if (!IsNumberExpr(call.Args[i])) bytecode.Emit(Op.ConvToFloat);
					}
					bytecode.Emit(builtIn);
					break;
				case CallExpr call when NonReversedBuiltInCalls.TryGetValue(call.Name, out var builtIn):
					for (var i = 0; i < call.Args.Count; ++i)
					{
						Emit(call.Args[i]);
						if (!IsNumberExpr(call.Args[i])) bytecode.Emit(Op.ConvToFloat);
					}
					bytecode.Emit(builtIn);
					break;
				case CallExpr { Name: "arraylen" or "sarraylen" } call:
					for (var i = call.Args.Count - 1; i >= 0; --i)
					{
						Emit(call.Args[i]);
						if (NeedsObjectConversion(call.Args[i])) bytecode.Emit(Op.ConvToObject);
					}
					bytecode.Emit(Op.ObjSize);
					break;
				case CallExpr { Name: "waitfor" } call:
					for (var i = 0; i < call.Args.Count; ++i)
					{
						Emit(call.Args[i]);
						if (i < 2 && NeedsStringConversion(call.Args[i])) bytecode.Emit(Op.ConvToString);
						else if (i == 2 && !IsNumberExpr(call.Args[i])) bytecode.Emit(Op.ConvToFloat);
					}
					bytecode.Emit(Op.WaitFor);
					break;
				case CallExpr { Name: "sleep" } call:
					for (var i = 0; i < call.Args.Count; ++i)
					{
						Emit(call.Args[i]);
						if (!IsNumberExpr(call.Args[i])) bytecode.Emit(Op.ConvToFloat);
					}
					bytecode.Emit(Op.Sleep);
					break;
				case CallExpr { Name: "setarray" } call:
					if (call.Args.Count > 0)
					{
						Emit(call.Args[0]);
						if (NeedsObjectConversion(call.Args[0])) bytecode.Emit(Op.ConvToObject);
					}
					if (call.Args.Count > 1)
					{
						Emit(call.Args[1]);
						if (!IsNumberExpr(call.Args[1])) bytecode.Emit(Op.ConvToFloat);
					}
					bytecode.Emit(Op.SetArray);
					break;
				case CallExpr { Name: "makevar" } call:
					for (var i = call.Args.Count - 1; i >= 0; --i)
					{
						Emit(call.Args[i]);
						bytecode.Emit(Op.ConvToString);
					}
					bytecode.Emit(Op.MakeVar);
					break;
				case CallExpr { Name: "format" } call:
					bytecode.Emit(Op.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					bytecode.Emit(Op.Format);
					break;
				case CallExpr call:
					bytecode.Emit(Op.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					bytecode.Emit(Op.TypeVar);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(call.Name));
					bytecode.Emit(Op.Call);
					break;
				case ChainedCallExpr call:
					bytecode.Emit(Op.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					Emit(call.Call);
					bytecode.Emit(Op.Call);
					break;
				case DynamicMethodCallExpr call:
					bytecode.Emit(Op.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					Emit(call.Object);
					if (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr) bytecode.Emit(Op.ConvToObject);
					_dynamicMethodName = true;
					if (call.Name is StringCastExpr { Expression: IdentifierExpr or MemberExpr { Object: IdentifierExpr { Name: "temp" } } } callNameCast)
					{
						Emit(callNameCast.Expression);
						if (NeedsNumericConversion(callNameCast.Expression)) bytecode.Emit(Op.ConvToFloat);
						bytecode.Emit(Op.Add);
					}
					else if (call.Name is StringCastExpr { Expression: BinaryExpr { Op: "@" } nameConcat }) Emit(nameConcat);
					else Emit(call.Name);
					_dynamicMethodName = false;
					if (NeedsStringConversion(call.Name) && call.Name is not BinaryExpr { Op: "@" }) bytecode.Emit(Op.ConvToString);
					bytecode.Emit(Op.MemberAccess);
					bytecode.Emit(Op.Call);
					break;
				case MethodCallExpr call:
					if (call.Name == "size" && call.Args.Count == 0)
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr) bytecode.Emit(Op.ConvToObject);
						bytecode.Emit(Op.ObjSize);
						break;
					}
					if (call.Name == "type" && call.Args.Count == 0)
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object)) bytecode.Emit(Op.ConvToObject);
						bytecode.Emit(Op.ObjType);
						break;
					}
					if (call.Name == "indices" && call.Args.Count == 0)
					{
						Emit(call.Object);
						bytecode.Emit(Op.ObjIndices);
						break;
					}
					if (call.Name == "link" && call.Args.Count == 0)
					{
						Emit(call.Object);
						bytecode.Emit(Op.ObjLink);
						break;
					}
					if (call.Name == "index")
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr) bytecode.Emit(Op.ConvToObject);
						foreach (var arg in call.Args) Emit(arg);
						bytecode.Emit(Op.ObjIndex);
						break;
					}
					if (call.Name == "length" && call.Args.Count == 0)
					{
						Emit(call.Object);
						bytecode.Emit(Op.ConvToString);
						bytecode.Emit(Op.ObjLength);
						break;
					}
					if (call.Name == "substring")
					{
						Emit(call.Object);
						bytecode.Emit(Op.ConvToString);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (!IsNumberExpr(arg)) bytecode.Emit(Op.ConvToFloat);
						}
						bytecode.Emit(Op.ObjSubstr);
						break;
					}
					if (call.Name == "pos")
					{
						Emit(call.Object);
						bytecode.Emit(Op.ConvToString);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (NeedsStringConversion(arg)) bytecode.Emit(Op.ConvToString);
						}
						bytecode.Emit(Op.ObjPos);
						break;
					}
					if (call.Name == "charat")
					{
						Emit(call.Object);
						bytecode.Emit(Op.ConvToString);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (!IsNumberExpr(arg)) bytecode.Emit(Op.ConvToFloat);
						}
						bytecode.Emit(Op.ObjCharAt);
						break;
					}
					if (call.Name is "starts" or "ends")
					{
						Emit(call.Object);
						bytecode.Emit(Op.ConvToString);
						foreach (var arg in call.Args) Emit(arg);
						bytecode.Emit(call.Name == "starts" ? Op.ObjStarts : Op.ObjEnds);
						break;
					}
					if (call.Name == "trim" && call.Args.Count == 0)
					{
						Emit(call.Object);
						bytecode.Emit(Op.ConvToString);
						bytecode.Emit(Op.ObjTrim);
						break;
					}
					if (call.Name == "tokenize")
					{
						Emit(call.Object);
						bytecode.Emit(Op.ConvToString);
						if (call.Args.Count == 0)
						{
							bytecode.Emit(Op.TypeString);
							bytecode.EmitDynamicStringIndex(bytecode.GetString(" ,"));
						}
						else foreach (var arg in call.Args) Emit(arg);
						bytecode.Emit(Op.ObjTokenize);
						break;
					}
					if (call.Name == "positions")
					{
						Emit(call.Object);
						bytecode.Emit(Op.ConvToString);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (NeedsStringConversion(arg)) bytecode.Emit(Op.ConvToString);
						}
						bytecode.Emit(Op.ObjPositions);
						break;
					}
					if (call.Name == "subarray")
					{
						for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
						Emit(call.Object);
						bytecode.Emit(Op.ObjSubarray);
						break;
					}
					if (call.Name == "clear" && call.Args.Count == 0)
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object)) bytecode.Emit(Op.ConvToObject);
						bytecode.Emit(Op.ObjClear);
						break;
					}
					if (call.Name is "add" or "delete")
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr) bytecode.Emit(Op.ConvToObject);
						foreach (var arg in call.Args) Emit(arg);
						bytecode.Emit(call.Name == "add" ? Op.ObjAddString : Op.ObjDeleteString);
						break;
					}
					if (call.Name is "insert" or "remove" or "replace")
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object)) bytecode.Emit(Op.ConvToObject);
						for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
						bytecode.Emit(call.Name switch
						{
							"insert" => Op.ObjInsertString,
							"remove" => Op.ObjRemoveString,
							_ => Op.ObjReplaceString,
						});
						break;
					}
					bytecode.Emit(Op.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					Emit(call.Object);
					if (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr) bytecode.Emit(Op.ConvToObject);
					bytecode.Emit(Op.TypeVar);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(call.Name));
					bytecode.Emit(Op.MemberAccess);
					bytecode.Emit(Op.Call);
					break;
				case NewObjectExpr obj:
					var typeNameIndex = bytecode.GetString(obj.TypeName);
					if (obj.Args.Count == 1)
					{
						Emit(obj.Args[0]);
						bytecode.Emit(Op.InlineNew);
					}
					else
					{
						bytecode.Emit(Op.TypeVar);
						bytecode.EmitDynamicStringIndex(bytecode.GetString("unknown_object"));
					}
					bytecode.Emit(Op.TypeString);
					bytecode.EmitDynamicStringIndex(typeNameIndex);
					bytecode.Emit(Op.NewObject);
					break;
				case NewArrayExpr array:
					for (var i = 0; i < array.Dimensions.Count; ++i)
					{
						Emit(array.Dimensions[i]);
						bytecode.Emit(i == 0 ? Op.ArrayNew : Op.ArrayNewMultiDim);
					}
					break;
				case LambdaExpr lambda:
					EmitFunction(new(lambda.Name, null, true, lambda.Args, lambda.Body), false);
					bytecode.Emit(Op.This);
					bytecode.Emit(Op.TypeVar);
					bytecode.EmitDynamicStringIndex(bytecode.GetString(lambda.Name));
					bytecode.Emit(Op.MemberAccess);
					bytecode.Emit(Op.ConvToObject);
					break;
				case ArrayLiteralExpr array:
					bytecode.Emit(Op.TypeArray);
					for (var i = array.Values.Count - 1; i >= 0; --i) Emit(array.Values[i]);
					bytecode.Emit(Op.ArrayEnd);
					break;
			}
		}

		private void EmitArrayIndex(ArrayIndexExpr expr, bool assignmentTarget)
		{
			Emit(expr.Target);
			if (expr.Target is not ArrayIndexExpr) bytecode.Emit(Op.ConvToObject);
			Emit(expr.Index);
			if (!IsNumberExpr(expr.Index)) bytecode.Emit(Op.ConvToFloat);
			if (!assignmentTarget) bytecode.Emit(Op.Array);
		}

		private void EmitTernary(TernaryExpr ternary, int? chainedSuccessLoc = null)
		{
			Emit(ternary.Condition, false, false, 0);
			if (!IsBooleanExpr(ternary.Condition)) bytecode.Emit(Op.ConvToFloat);
			if (chainedSuccessLoc.HasValue) bytecode.PatchShort(chainedSuccessLoc.Value, bytecode.OpIndex);
			bytecode.Emit(Op.If);
			var failLoc = bytecode.EmitNumberOperandPlaceholder();
			Emit(ternary.WhenTrue);
			bytecode.PatchShort(failLoc, bytecode.OpIndex + 1);
			bytecode.Emit(Op.SetIndex);
			var successLoc = bytecode.EmitNumberOperandPlaceholder();
			if (ternary.WhenFalse is TernaryExpr nested) EmitTernary(nested, successLoc);
			else
			{
				Emit(ternary.WhenFalse);
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
				Emit(terms[i], false, false, 0);
				if (!IsBooleanExpr(terms[i])) bytecode.Emit(Op.ConvToFloat);
				bytecode.Emit(logical.Op == "&&" ? Op.And : Op.Or);
				patches.Add(bytecode.EmitNumberOperandPlaceholder());
			}
			Emit(terms[^1], false, false, 0);
			if (!IsBooleanExpr(terms[^1])) bytecode.Emit(Op.ConvToFloat);
			if (logicalInline) bytecode.Emit(Op.InlineConditional);
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
			bytecode.Emit(Op.ConvToObject);
			foreach (var index in expr.Indices)
			{
				Emit(index);
				if (!IsNumberExpr(index)) bytecode.Emit(Op.ConvToFloat);
			}
			if (!assignmentTarget) bytecode.Emit(Op.ArrayMultiDim);
		}

		private static bool IsCompoundAssign(string op) => op is "+=" or "-=" or "*=" or "/=" or "^=" or "%=" or "@=" or "<<=" or ">>=";

		private static readonly Dictionary<string, Op> BuiltInCalls = new(StringComparer.Ordinal)
		{
			["sin"] = Op.Sin,
			["char"] = Op.Char,
			["cos"] = Op.Cos,
			["arctan"] = Op.Arctan,
			["vecx"] = Op.Vecx,
			["vecy"] = Op.Vecy,
			["abs"] = Op.Abs,
			["exp"] = Op.Exp,
			["log"] = Op.Log,
			["random"] = Op.Random,
			["min"] = Op.Min,
			["max"] = Op.Max,
		};

		private static readonly Dictionary<string, Op> NonReversedBuiltInCalls = new(StringComparer.Ordinal)
		{
			["pow"] = Op.Pow,
			["getangle"] = Op.GetAngle,
			["getdir"] = Op.GetDir,
		};

		private static readonly HashSet<string> NonReturningBuiltInCalls = new(StringComparer.Ordinal)
		{
			"sleep",
			"setarray",
		};

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

		private static bool NeedsNumericConversion(Expr expr) => ExpressionTypeOf(expr) != ExprType.Number;
		private static bool NeedsStringConversion(Expr expr) => ExpressionTypeOf(expr) != ExprType.String;

		private string NextNegativeFloatText(string text)
		{
			var previous = _negativeFloatText.TryGetValue(text, out var value) ? value : text;
			var next = "-" + previous;
			_negativeFloatText[text] = next;
			return next;
		}

		private string CurrentFloatText(string text) => _negativeFloatText.TryGetValue(text, out var value) ? value : text;

		private static bool NeedsObjectConversion(Expr expr) => expr switch
		{
			IdentifierExpr { Name: "this" or "thiso" or "player" or "playero" or "level" or "temp" } => false,
			ArrayIndexExpr => false,
			_ => true,
		};

		private static bool IsNumberExpr(Expr expr) => ExpressionTypeOf(expr) == ExprType.Number;

		private static bool UsesInlineForCondition(Expr expr) => expr is BinaryExpr { Op: "&&", Left: BinaryExpr { Op: ">", Left: MemberExpr { Object: IdentifierExpr { Name: "temp" } }, Right: UnaryExpr { Op: "-", Expression: NumberExpr { Text: "1" } } }, Right: UnaryExpr { Op: "!", Expression: MemberExpr { Object: IdentifierExpr { Name: "temp" } } } }
			or BinaryExpr { Op: "&&", Left: BinaryExpr { Op: "<", Left: MemberExpr { Object: IdentifierExpr { Name: "temp" }, Name: "i" }, Right: MemberExpr { Object: IdentifierExpr { Name: "temp" }, Name: "frames" } }, Right: BinaryExpr { Op: ">", Left: IdentifierExpr { Name: "ft" }, Right: MemberExpr { Object: IdentifierExpr { Name: "temp" }, Name: "anilength" } } };

		private static bool UsesSignTernary(Expr expr) => expr is TernaryExpr { Condition: BinaryExpr { Op: "<", Left: MemberExpr { Object: IdentifierExpr { Name: "temp" }, Name: "t" }, Right: NumberExpr { Text: "0" } }, WhenTrue: UnaryExpr { Op: "-", Expression: NumberExpr { Text: "1" } }, WhenFalse: NumberExpr { Text: "1" } };

		private static bool UsesPreviewSpriteTernary(Expr expr) => expr is TernaryExpr { Condition: MemberExpr { Object: IdentifierExpr { Name: "this" }, Name: "previewsprites" }, WhenTrue: NumberExpr { Text: "1" }, WhenFalse: NumberExpr { Text: "0.05" } };

		private static ExprType ExpressionTypeOf(Expr expr) => expr switch
		{
			NumberExpr or BoolExpr or InExpr or UnaryExpr { Op: "-" or "!" or "~" or "++" or "--" } or CastExpr { Type: "int" or "float" } => ExprType.Number,
			BinaryExpr { Op: "+" or "-" or "*" or "/" or "%" or "^" or "==" or "!=" or "<" or "<=" or "=<" or ">" or ">=" or "=>" or "&&" or "||" or "&" or "|" or "xor" or "<<" or ">>" } => ExprType.Number,
			BinaryExpr { Op: "+=" or "-=" or "*=" or "/=" or "%=" or "^=" or "<<=" or ">>=" or "|=" or "&=" } => ExprType.Number,
			BinaryExpr { Op: "@" or " " or "\n" or "\t" } or StringExpr or CastExpr { Type: "_" } => ExprType.String,
			StringCastExpr => ExprType.String,
			BinaryExpr { Op: "@=" } => ExprType.String,
			BinaryExpr { Op: "=", Right: var right } => ExpressionTypeOf(right),
			TernaryExpr { WhenTrue: var trueExpr, WhenFalse: var falseExpr } when ExpressionTypeOf(trueExpr) == ExpressionTypeOf(falseExpr) => ExpressionTypeOf(trueExpr),
			ArrayLiteralExpr => ExprType.Array,
			NewObjectExpr or NewArrayExpr or NullExpr => ExprType.Object,
			_ => ExprType.Unknown,
		};

		private static bool IsBooleanExpr(Expr expr) => expr switch
		{
			BinaryExpr { Op: "==" or "!=" or "<" or "<=" or "=<" or ">" or ">=" or "=>" or "&&" or "||" } => true,
			InExpr => true,
			UnaryExpr { Op: "!" } => true,
			_ => false,
		};

		private static bool ContainsCall(Stmt statement) => statement switch
		{
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
				if (arg is IdentifierExpr id) aliases.Remove(id.Name);
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
							if (label != null) CollectTempAliases(label, aliases);
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

		private static bool ContainsCall(Expr expr) => expr switch
		{
			CallExpr => true,
			ChainedCallExpr => true,
			MethodCallExpr => true,
			DynamicMethodCallExpr => true,
			NewObjectExpr obj => obj.Args.Count > 0,
			InExpr inExpr => ContainsCall(inExpr.Expression) || ContainsCall(inExpr.Lower) || (inExpr.Upper != null && ContainsCall(inExpr.Upper)),
			TernaryExpr ternary => ContainsCall(ternary.Condition) || ContainsCall(ternary.WhenTrue) || ContainsCall(ternary.WhenFalse),
			BinaryExpr binary => ContainsCall(binary.Left) || ContainsCall(binary.Right),
			UnaryExpr unary => ContainsCall(unary.Expression),
			StringCastExpr cast => ContainsCall(cast.Expression),
			CastExpr cast => ContainsCall(cast.Expression),
			MemberExpr member => ContainsCall(member.Object),
			DynamicMemberExpr member => ContainsCall(member.Object) || ContainsCall(member.Name),
			DynamicVarExpr dynamicVar => ContainsCall(dynamicVar.Name),
			ArrayIndexExpr index => ContainsCall(index.Target) || ContainsCall(index.Index),
			MultiArrayIndexExpr index => ContainsCall(index.Target) || index.Indices.Exists(ContainsCall),
			_ => false,
		};

		private static Op BinaryOpcode(string op) => op switch
		{
			"+" => Op.Add,
			"-" => Op.Sub,
			"*" => Op.Mul,
			"/" => Op.Div,
			"%" => Op.Mod,
			"^" => Op.Pow,
			"@" => Op.Join,
			"==" => Op.Eq,
			"!=" => Op.Neq,
			"<" => Op.Lt,
			"<=" or "=<" => Op.Lte,
			">" => Op.Gt,
			">=" or "=>" => Op.Gte,
			"&" => Op.BitAnd,
			"|" => Op.BitOr,
			"xor" => Op.BitXor,
			"<<" => Op.ShiftLeft,
			">>" => Op.ShiftRight,
			"&&" => Op.And,
			"||" => Op.Or,
			_ => Op.None,
		};

		private static Op CompoundOpcode(string op) => op switch
		{
			"<<=" => Op.ShiftLeft,
			">>=" => Op.ShiftRight,
			"^=" => Op.Pow,
			_ => BinaryOpcode(op[0].ToString()),
		};
	}

	private sealed class BytecodeWriter
	{
		private readonly int _gs1EventFlags;
		private readonly List<byte> _code = [];
		private readonly List<string> _strings = [];
		private readonly Dictionary<string, int> _stringIndex = new(StringComparer.Ordinal);
		private readonly List<FunctionEntry> _functions = [];
		private readonly HashSet<string> _functionSet = new(StringComparer.Ordinal);
		private readonly List<int> _prejumpPatches = [];
		private Op _lastOp = Op.None;
		public int OpIndex { get; private set; }

		public BytecodeWriter(int gs1EventFlags) => _gs1EventFlags = gs1EventFlags;

		public int GetString(string value)
		{
			if (_stringIndex.TryGetValue(value, out var index)) return index;
			index = _strings.Count;
			_strings.Add(value);
			_stringIndex[value] = index;
			return index;
		}

		public bool AddFunction(string name, int opIndex, int jmpLoc)
		{
			if (!_functionSet.Add(name)) return false;
			_functions.Add(new(name, opIndex, jmpLoc));
			return true;
		}

		public void AddPrejumpPatch(int pos) => _prejumpPatches.Add(pos);

		public void Emit(Op op)
		{
			_code.Add((byte)op);
			_lastOp = op;
			OpIndex++;
		}

		public void EmitNumberOperand(int value)
		{
			_code.Add(0xF4);
			WriteShort(_code, value);
		}

		public int EmitNumberOperandPlaceholder()
		{
			_code.Add(0xF4);
			var pos = _code.Count;
			WriteShort(_code, 0);
			return pos;
		}

		public void PatchShort(int pos, int value)
		{
			_code[pos] = (byte)((value >> 8) & 0xFF);
			_code[pos + 1] = (byte)(value & 0xFF);
		}

		public void EmitDynamicStringIndex(int value)
		{
			if (value <= byte.MaxValue) { _code.Add(0xF0); _code.Add((byte)value); }
			else if (value <= ushort.MaxValue) { _code.Add(0xF1); WriteShort(_code, value); }
			else { _code.Add(0xF2); WriteInt(_code, value); }
		}

		public void EmitDynamicNumber(int value)
		{
			var offset = _lastOp == Op.TypeNumber || _lastOp == Op.SetIndex || _lastOp == Op.SetIndexTrue ? 3 : 0;
			if (value >= sbyte.MinValue && value <= sbyte.MaxValue) { _code.Add((byte)(0xF0 + offset)); _code.Add(unchecked((byte)(sbyte)value)); }
			else if (value >= short.MinValue && value <= short.MaxValue) { _code.Add((byte)(0xF1 + offset)); WriteShort(_code, value); }
			else { _code.Add((byte)(0xF2 + offset)); WriteInt(_code, value); }
		}

		public void EmitDoubleNumber(string value)
		{
			_code.Add(0xF6);
			WriteCString(_code, value);
		}

		public byte[] ToArray()
		{
			Emit(Op.Ret);
			foreach (var loc in _prejumpPatches) PatchShort(loc, OpIndex);
			List<byte> result = [];
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
			result.Add(10);
			return [.. result];
		}

		private IEnumerable<FunctionEntry> OrderedFunctions()
		{
			HashSet<string> emitted = new(StringComparer.Ordinal);
			foreach (var fn in _strings.SelectMany(str => _functions.Where(fn => fn.Name == str && emitted.Add(fn.Name)))) yield return fn;
			foreach (var fn in _functions.Where(fn => emitted.Add(fn.Name))) yield return fn;
		}

		private static void WriteSegment(List<byte> target, BytecodeSegment type, List<byte> bytes)
		{
			WriteInt(target, (int)type);
			WriteInt(target, bytes.Count);
			target.AddRange(bytes);
		}

		private static void WriteCString(List<byte> target, string value)
		{
			target.AddRange(Encoding.UTF8.GetBytes(value));
			target.Add(0);
		}

		private static void WriteShort(List<byte> target, int value)
		{
			target.Add((byte)((value >> 8) & 0xFF));
			target.Add((byte)(value & 0xFF));
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
			var info = Encoding.UTF8.GetBytes($"{type},{name},0,");
			List<byte> result = [0xAC, (byte)((info.Length >> 8) & 0xFF), (byte)(info.Length & 0xFF)];
			result.AddRange(info);
			result.AddRange(bytecode);
			return [.. result];
		}
	}

	private sealed record FunctionEntry(string Name, int OpIndex, int JmpLoc);
	private enum ExprType { Unknown, Number, String, Array, Object }

	private enum Op : byte
	{
		None = 0,
		SetIndex = 1,
		SetIndexTrue = 2,
		Or = 3,
		If = 4,
		And = 5,
		Call = 6,
		Sleep = 8,
		CmdCall = 9,
		Jmp = 10,
		WaitFor = 11,
		TypeNumber = 20,
		TypeString = 21,
		TypeVar = 22,
		TypeArray = 23,
		TypeTrue = 24,
		TypeFalse = 25,
		TypeNull = 26,
		Pi = 27,
		SwapLastOps = 31,
		IndexDec = 32,
		ConvToFloat = 33,
		ConvToString = 34,
		MemberAccess = 35,
		ConvToObject = 36,
		ArrayEnd = 37,
		ArrayNew = 38,
		SetArray = 39,
		InlineNew = 40,
		MakeVar = 41,
		NewObject = 42,
		Assign = 50,
		FuncParamsEnd = 51,
		Inc = 52,
		Dec = 53,
		CopyLastOp = 30,
		Add = 60,
		Sub = 61,
		Mul = 62,
		Div = 63,
		Mod = 64,
		Pow = 65,
		Not = 68,
		UnarySub = 69,
		Eq = 70,
		Neq = 71,
		Lt = 72,
		Gt = 73,
		Lte = 74,
		Gte = 75,
		BitOr = 76,
		BitAnd = 77,
		BitXor = 78,
		BitInvert = 79,
		InRange = 80,
		InObj = 81,
		ObjIndex = 82,
		ObjType = 83,
		Format = 84,
		Abs = 86,
		Int = 85,
		Random = 87,
		Sin = 88,
		Cos = 89,
		Arctan = 90,
		Exp = 91,
		Log = 92,
		Min = 93,
		Max = 94,
		GetAngle = 95,
		GetDir = 96,
		Vecx = 97,
		Vecy = 98,
		ObjIndices = 99,
		ObjLink = 100,
		ShiftLeft = 101,
		ShiftRight = 102,
		Char = 103,
		ObjTrim = 110,
		ObjLength = 111,
		ObjPos = 112,
		Join = 113,
		ObjCharAt = 114,
		ObjSubstr = 115,
		ObjStarts = 116,
		ObjEnds = 117,
		ObjTokenize = 118,
		Translate = 119,
		ObjPositions = 120,
		ObjSize = 130,
		Array = 131,
		ArrayAssign = 132,
		ArrayMultiDim = 133,
		ArrayMultiDimAssign = 134,
		ObjSubarray = 135,
		ObjAddString = 136,
		ObjDeleteString = 137,
		ObjRemoveString = 138,
		ObjReplaceString = 139,
		ObjInsertString = 140,
		ObjClear = 141,
		ArrayNewMultiDim = 142,
		With = 150,
		WithEnd = 151,
		Foreach = 163,
		InlineConditional = 44,
		Ret = 7,
		This = 180,
		Thiso = 181,
		Player = 182,
		Playero = 183,
		Level = 184,
		Temp = 189,
	}
}
