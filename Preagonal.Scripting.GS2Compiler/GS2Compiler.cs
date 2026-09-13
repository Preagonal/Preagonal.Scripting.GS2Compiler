using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Preagonal.Scripting.GS2Compiler;

internal static class GS2Compiler
{
	public static CompilerResponse Compile(string code, string type, string name, bool withHeader)
	{
		var parser = new Parser(code);
		var program = parser.Parse();
		if (parser.ErrorMessage != null) return new() { Success = false, ErrMsg = parser.ErrorMessage, ByteCode = [] };
		var bytecode = new BytecodeWriter();
		var emitter = new Emitter(bytecode, program.Constants, program.Enums);
		foreach (var item in program.Items)
		{
			if (item is FunctionItem function) emitter.EmitFunction(function.Function);
			else if (item is StatementItem statement) emitter.EmitStatement(statement.Statement);
		}
		var result = bytecode.ToArray();
		return new() { Success = true, ErrMsg = null, ByteCode = withHeader ? Header.Wrap(result, type, name) : result };
	}

	private sealed class Parser
	{
		private readonly Lexer _lexer;
		private Token _current;
		private Token _previous = new(TokenType.End, "", 1, 1);
		private readonly Dictionary<string, Expr> _constants = new(StringComparer.Ordinal);
		private readonly Dictionary<string, Dictionary<string, int>> _enums = new(StringComparer.Ordinal);
		private int _lambdaId = 100;
		public string? ErrorMessage { get; private set; }

		public Parser(string code)
		{
			_lexer = new(code);
			_current = _lexer.Next();
		}

		public ProgramNode Parse()
		{
			List<FunctionNode> functions = [];
			List<ProgramItem> items = [];
			while (_current.Type != TokenType.End)
			{
				if (Match(TokenType.Const)) ParseConst();
				else if (Match(TokenType.Enum)) ParseEnum();
				else if (Match(TokenType.Public)) { Expect(TokenType.Function); items.Add(new FunctionItem(ParseFunction(true, null))); }
				else if (Match(TokenType.Function)) items.Add(new FunctionItem(ParseFunction(false, null)));
				else items.Add(new StatementItem(ParseStatement()));
			}
			return new(_constants, _enums, items);
		}

		private void ParseConst()
		{
			var name = Expect(TokenType.Identifier).Text;
			Expect(TokenType.Assign);
			_constants[name] = ParseExpression();
			Expect(TokenType.Semicolon);
		}

		private void ParseEnum()
		{
			var enumName = Match(TokenType.Identifier) ? _previous.Text : "";
			Expect(TokenType.LeftBrace);
			Dictionary<string, int> values = new(StringComparer.Ordinal);
			var index = 0;
			while (_current.Type != TokenType.RightBrace && _current.Type != TokenType.End)
			{
				var name = Expect(TokenType.Identifier).Text;
				if (Match(TokenType.Assign)) index = ParseSignedInt();
				values[name] = index++;
				Match(TokenType.Comma);
			}
			Expect(TokenType.RightBrace);
			Match(TokenType.Semicolon);
			if (enumName.Length > 0) _enums[enumName] = values;
		}

		private FunctionNode ParseFunction(bool isPublic, string? objectName)
		{
			var name = ParseQualifiedIdentifierName();
			if (Match(TokenType.Dot))
			{
				objectName = name;
				name = ParseQualifiedIdentifierName();
			}
			Expect(TokenType.LeftParen);
			List<Expr> args = [];
			if (_current.Type != TokenType.RightParen)
			{
				do args.Add(ParseExpression()); while (Match(TokenType.Comma));
			}
			Expect(TokenType.RightParen);
			var body = _current.Type switch
			{
				TokenType.LeftBrace => ParseBlock(),
				TokenType.Semicolon => EmptyBody(),
				TokenType.Function or TokenType.End => [],
				_ => [ParseStatement()]
			};
			return new(name, objectName, isPublic, args, body);
		}

		private List<Stmt> EmptyBody()
		{
			Match(TokenType.Semicolon);
			return [];
		}

		private List<Stmt> ParseBody() => _current.Type == TokenType.LeftBrace ? ParseBlock() : [new InlineStmt(ParseStatement())];

		private List<Stmt> ParseBlock()
		{
			List<Stmt> statements = [];
			Expect(TokenType.LeftBrace);
			while (_current.Type != TokenType.RightBrace && _current.Type != TokenType.End)
				statements.Add(ParseStatement());
			Expect(TokenType.RightBrace);
			return statements;
		}

		private Stmt ParseStatement()
		{
			if (Match(TokenType.Semicolon)) return new BlockStmt([]);
			if (_current.Type == TokenType.LeftBrace) return new BlockStmt(ParseBlock());
			if (Match(TokenType.New)) return ParseNewStatement();
			if (Match(TokenType.Return))
			{
				var expr = _current.Type == TokenType.Semicolon ? new NumberExpr("0") : ParseExpression();
				Expect(TokenType.Semicolon);
				return new ReturnStmt(expr);
			}
			if (Match(TokenType.Break))
			{
				Expect(TokenType.Semicolon);
				return new BreakStmt();
			}
			if (Match(TokenType.Continue))
			{
				Expect(TokenType.Semicolon);
				return new ContinueStmt();
			}
			if (Match(TokenType.If))
			{
				Expect(TokenType.LeftParen);
				var condition = ParseExpression();
				Expect(TokenType.RightParen);
				var thenBody = ParseBody();
				var hasElse = false;
				var elseBody = Match(TokenType.Else) ? HasElse(ParseBody(), out hasElse) : Match(TokenType.ElseIf) ? HasElse([ParseElseIfStatement()], out hasElse) : [];
				return new IfStmt(condition, thenBody, elseBody, hasElse);
			}
			if (Match(TokenType.For)) return ParseForStatement();
			if (Match(TokenType.While))
			{
				Expect(TokenType.LeftParen);
				var condition = ParseExpression();
				Expect(TokenType.RightParen);
				return new WhileStmt(condition, ParseBody());
			}
			if (Match(TokenType.With))
			{
				Expect(TokenType.LeftParen);
				var target = ParseExpression();
				Expect(TokenType.RightParen);
				return new WithStmt(target, ParseBody());
			}
			if (Match(TokenType.Switch)) return ParseSwitchStatement();
			var before = _current;
			var exprStatement = new ExprStmt(ParseExpression());
			Expect(TokenType.Semicolon);
			if (before == _current) Advance();
			return exprStatement;
		}

		private Stmt ParseNewStatement()
		{
			var typeName = ParseQualifiedIdentifierName();
			Expect(TokenType.LeftParen);
			List<Expr> args = [];
			if (_current.Type != TokenType.RightParen)
				do args.Add(ParseExpression()); while (Match(TokenType.Comma));
			Expect(TokenType.RightParen);
			return new NewStmt(typeName, args, _current.Type == TokenType.LeftBrace ? ParseBlock() : []);
		}

		private Stmt ParseElseIfStatement()
		{
			Expect(TokenType.LeftParen);
			var condition = ParseExpression();
			Expect(TokenType.RightParen);
			var thenBody = ParseBody();
			var hasElse = false;
			var elseBody = Match(TokenType.Else) ? HasElse(ParseBody(), out hasElse) : Match(TokenType.ElseIf) ? HasElse([ParseElseIfStatement()], out hasElse) : [];
			return new IfStmt(condition, thenBody, elseBody, hasElse);
		}

		private static List<Stmt> HasElse(List<Stmt> body, out bool hasElse)
		{
			hasElse = true;
			return body;
		}

		private Stmt ParseForStatement()
		{
			Expect(TokenType.LeftParen);
			var first = _current.Type == TokenType.Semicolon ? null : ParseExpression();
			if (Match(TokenType.Colon))
			{
				if (first == null) Error("malformed input");
				var source = ParseExpression();
				Expect(TokenType.RightParen);
				return new ForEachStmt(first ?? new NumberExpr("0"), source, ParseBody());
			}
			Expect(TokenType.Semicolon);
			var condition = _current.Type == TokenType.Semicolon ? new BoolExpr(true) : ParseExpression();
			Expect(TokenType.Semicolon);
			var post = _current.Type == TokenType.RightParen ? null : ParseExpression();
			Expect(TokenType.RightParen);
			return new ForStmt(first, condition, post, ParseBody());
		}

		private Stmt ParseSwitchStatement()
		{
			Expect(TokenType.LeftParen);
			var expr = ParseExpression();
			Expect(TokenType.RightParen);
			Expect(TokenType.LeftBrace);
			List<SwitchCase> cases = [];
			while (_current.Type != TokenType.RightBrace && _current.Type != TokenType.End)
			{
				List<Expr?> labels = [];
				while (_current.Type is TokenType.Case or TokenType.Default)
				{
					if (Match(TokenType.Case)) labels.Add(ParseExpression());
					else { Match(TokenType.Default); labels.Add(null); }
					Expect(TokenType.Colon);
				}
				List<Stmt> statements = [];
				while (_current.Type is not TokenType.Case and not TokenType.Default and not TokenType.RightBrace and not TokenType.End)
					statements.Add(ParseStatement());
				if (labels.Count > 1) labels.Reverse();
				cases.Add(new(labels, statements));
			}
			Expect(TokenType.RightBrace);
			return new SwitchStmt(expr, cases);
		}

		private Expr ParseExpression() => ParseAssignment();

		private Expr ParseAssignment()
		{
			var left = ParseConditional();
			var op = _current.Type switch
			{
				TokenType.Assign => "=",
				TokenType.AddAssign => "+=",
				TokenType.SubAssign => "-=",
				TokenType.MulAssign => "*=",
				TokenType.DivAssign => "/=",
				TokenType.PowAssign => "^=",
				TokenType.ModAssign => "%=",
				TokenType.CatAssign => "@=",
				TokenType.ShiftLeftAssign => "<<=",
				TokenType.ShiftRightAssign => ">>=",
				_ => ""
			};
			if (op.Length == 0) return left;
			Advance();
			return new BinaryExpr(left, op, ParseAssignment());
		}

		private Expr ParseConditional()
		{
			var condition = ParseLogicalOr();
			if (!Match(TokenType.Question)) return condition;
			var whenTrue = ParseExpression();
			Expect(TokenType.Colon);
			return new TernaryExpr(condition, whenTrue, ParseExpression());
		}

		private Expr ParseLogicalOr()
		{
			var expr = ParseLogicalAnd();
			while (Match(TokenType.Or)) expr = new BinaryExpr(expr, "||", ParseLogicalAnd());
			return expr;
		}

		private Expr ParseLogicalAnd()
		{
			var expr = ParseBitwise();
			while (Match(TokenType.And)) expr = new BinaryExpr(expr, "&&", ParseBitwise());
			return expr;
		}

		private Expr ParseBitwise()
		{
			var expr = ParseEquality();
			while (_current.Type is TokenType.BitAnd or TokenType.BitOr or TokenType.BitXor or TokenType.ShiftLeft or TokenType.ShiftRight)
			{
				var op = _current.Text;
				Advance();
				expr = new BinaryExpr(expr, op, ParseEquality());
			}
			return expr;
		}

		private Expr ParseEquality()
		{
			var expr = ParseComparison();
			while (_current.Type is TokenType.Equal or TokenType.NotEqual or TokenType.In)
			{
				if (Match(TokenType.In))
				{
					if (Match(TokenType.BitOr))
					{
						var lower = ParseComparison();
						Expect(TokenType.Comma);
						var upper = ParseComparison();
						Expect(TokenType.BitOr);
						expr = new InExpr(expr, lower, upper);
					}
					else if (Match(TokenType.Less))
					{
						var lower = ParseComparison();
						Expect(TokenType.Comma);
						var upper = ParseComparison();
						Expect(TokenType.Greater);
						expr = new InExpr(expr, lower, upper);
					}
					else expr = new InExpr(expr, ParseComparison(), null);
					continue;
				}
				var op = _current.Text;
				Advance();
				expr = new BinaryExpr(expr, op, ParseComparison());
			}
			return expr;
		}

		private Expr ParseComparison()
		{
			var expr = ParseConcat();
			while (_current.Type is TokenType.Less or TokenType.LessEqual or TokenType.Greater or TokenType.GreaterEqual)
			{
				var op = _current.Text;
				Advance();
				expr = new BinaryExpr(expr, op, ParseConcat());
			}
			return expr;
		}

		private Expr ParseConcat()
		{
			var expr = ParseTerm();
			while (_current.Type is TokenType.At)
			{
				var op = _current.Text;
				Advance();
				expr = new BinaryExpr(expr, op, ParseTerm());
			}
			return expr;
		}

		private Expr ParseTerm()
		{
			var expr = ParseFactor();
			while (_current.Type is TokenType.Plus or TokenType.Minus)
			{
				var op = _current.Text;
				Advance();
				expr = new BinaryExpr(expr, op, ParseFactor());
			}
			return expr;
		}

		private Expr ParseFactor()
		{
			var expr = ParseUnary();
			while (_current.Type is TokenType.Star or TokenType.Slash or TokenType.Percent or TokenType.Caret)
			{
				var op = _current.Text;
				Advance();
				expr = new BinaryExpr(expr, op, ParseUnary());
			}
			return expr;
		}

		private Expr ParsePostfix()
		{
			Expr expr = ParsePrimary();
			while (true)
			{
				if (Match(TokenType.Dot))
				{
					if (Match(TokenType.LeftParen))
					{
						var name = Match(TokenType.At) ? new StringCastExpr(ParseExpression()) : ParseExpression();
						Expect(TokenType.RightParen);
						expr = new DynamicMemberExpr(expr, name);
						continue;
					}
					expr = new MemberExpr(expr, Expect(TokenType.Identifier).Text);
					continue;
				}
				if (Match(TokenType.LeftBracket))
				{
					List<Expr> indices = [ParseExpression()];
					while (Match(TokenType.Comma)) indices.Add(ParseExpression());
					Expect(TokenType.RightBracket);
					expr = indices.Count == 1 ? new ArrayIndexExpr(expr, indices[0]) : new MultiArrayIndexExpr(expr, indices);
					continue;
				}
				if (Match(TokenType.LeftParen))
				{
					List<Expr> args = [];
					if (_current.Type != TokenType.RightParen)
					{
						do args.Add(ParseExpression()); while (Match(TokenType.Comma));
					}
					Expect(TokenType.RightParen);
					expr = expr switch
					{
						IdentifierExpr id => new CallExpr(id.Name, args),
						MemberExpr member => new MethodCallExpr(member.Object, member.Name, args),
						CallExpr call => new ChainedCallExpr(call, args),
						DynamicMemberExpr member => new DynamicMethodCallExpr(member.Object, member.Name, args),
						_ => expr
					};
					continue;
				}
				break;
			}
			if (Match(TokenType.Increment)) expr = new UnaryExpr("++", expr, true);
			else if (Match(TokenType.Decrement)) expr = new UnaryExpr("--", expr, true);
			return expr;
		}

		private Expr ParseUnary()
		{
			if (Match(TokenType.Increment)) return new UnaryExpr("++", ParseUnary());
			if (Match(TokenType.Decrement)) return new UnaryExpr("--", ParseUnary());
			if (Match(TokenType.At)) return new StringCastExpr(ParseUnary());
			if (Match(TokenType.Minus)) return new UnaryExpr("-", ParseUnary());
			if (Match(TokenType.Not)) return new UnaryExpr("!", ParseUnary());
			if (Match(TokenType.BitInvert)) return new UnaryExpr("~", ParseUnary());
			return ParsePostfix();
		}

		private Expr ParsePrimary()
		{
			if (Match(TokenType.Number)) return new NumberExpr(_previous.Text);
			if (Match(TokenType.String))
			{
				if (_previous.Text == "\\") Error("malformed input");
				return new StringExpr(_previous.Text);
			}
			if (Match(TokenType.IntCast)) return ParseCast("int");
			if (Match(TokenType.FloatCast)) return ParseCast("float");
			if (Match(TokenType.Translate)) return ParseCast("_");
			if (Match(TokenType.True)) return new BoolExpr(true);
			if (Match(TokenType.False)) return new BoolExpr(false);
			if (Match(TokenType.Null)) return new NullExpr();
			if (Match(TokenType.New))
			{
				if (_current.Type == TokenType.LeftBracket)
				{
					List<Expr> dimensions = [];
					do
					{
						Expect(TokenType.LeftBracket);
						dimensions.Add(ParseExpression());
						Expect(TokenType.RightBracket);
					}
					while (_current.Type == TokenType.LeftBracket);
					return new NewArrayExpr(dimensions);
				}
				var typeName = ParseQualifiedIdentifierName();
				Expect(TokenType.LeftParen);
				List<Expr> args = [];
				if (_current.Type != TokenType.RightParen)
					do args.Add(ParseExpression()); while (Match(TokenType.Comma));
				Expect(TokenType.RightParen);
				return new NewObjectExpr(typeName, args);
			}
			if (Match(TokenType.Function))
			{
				Expect(TokenType.LeftParen);
				List<Expr> args = [];
				if (_current.Type != TokenType.RightParen)
				{
					do args.Add(ParseExpression()); while (Match(TokenType.Comma));
				}
				Expect(TokenType.RightParen);
				return new LambdaExpr($"function_{_lambdaId++}_1", args, ParseBody());
			}
			if (Match(TokenType.LeftBrace))
			{
				List<Expr> values = [];
				if (_current.Type != TokenType.RightBrace)
				{
					do
					{
						if (_current.Type == TokenType.RightBrace) break;
						values.Add(ParseExpression());
					}
					while (Match(TokenType.Comma));
				}
				Expect(TokenType.RightBrace);
				return new ArrayLiteralExpr(values);
			}
			if (Match(TokenType.LeftParen))
			{
				var expr = ParseExpression();
				Expect(TokenType.RightParen);
				return expr;
			}
			if (Match(TokenType.Identifier))
			{
				var first = _previous.Text;
				if (Match(TokenType.Scope))
				{
					List<string> parts = [first, Expect(TokenType.Identifier).Text];
					while (Match(TokenType.Scope)) parts.Add(Expect(TokenType.Identifier).Text);
					return parts.Count == 2 && !first.StartsWith('$') ? new EnumExpr(parts[0], parts[1]) : new IdentifierExpr(string.Join("::", parts));
				}
				return new IdentifierExpr(first);
			}
			Error("malformed input");
			return new NumberExpr("0");
		}

		private string ParseQualifiedIdentifierName()
		{
			var name = Expect(TokenType.Identifier).Text;
			while (Match(TokenType.Scope)) name += "::" + Expect(TokenType.Identifier).Text;
			return name;
		}

		private Expr ParseCast(string type)
		{
			Expect(TokenType.LeftParen);
			var expression = ParseExpression();
			Expect(TokenType.RightParen);
			return new CastExpr(type, expression);
		}

		private int ParseSignedInt()
		{
			var sign = Match(TokenType.Minus) ? -1 : 1;
			var token = Expect(TokenType.Number);
			return sign * int.Parse(token.Text, CultureInfo.InvariantCulture);
		}

		private bool Match(TokenType type)
		{
			if (_current.Type != type) return false;
			Advance();
			return true;
		}

		private Token Expect(TokenType type)
		{
			if (_current.Type == type)
			{
				var token = _current;
				Advance();
				return token;
			}
			Error("malformed input");
			return new(type, "", _current.Line, _current.Column);
		}

		private void Advance()
		{
			_previous = _current;
			_current = _lexer.Next();
		}

		private void Error(string message)
		{
			if (ErrorMessage != null) return;
			ErrorMessage = $"{message} at line {_current.Line}: {_current.LineText}\n";
		}
	}

	private sealed class Emitter
	{
		private readonly BytecodeWriter _bytecode;
		private readonly Dictionary<string, Expr> _constants;
		private readonly Dictionary<string, Dictionary<string, int>> _enums;
		private readonly Stack<List<int>> _breakPatches = new();
		private readonly Stack<List<int>> _continuePatches = new();
		private readonly Stack<List<int>> _conditionFailPatches = new();
		private readonly Stack<List<int>> _conditionSuccessPatches = new();
		private readonly Dictionary<string, string> _negativeFloatText = new(StringComparer.Ordinal);
		private int _newObjectCount;
		private bool _dynamicMethodName;

		public Emitter(BytecodeWriter bytecode, Dictionary<string, Expr> constants, Dictionary<string, Dictionary<string, int>> enums)
		{
			_bytecode = bytecode;
			_constants = constants;
			_enums = enums;
		}

		public void EmitFunction(FunctionNode node, bool patchToFinal = true)
		{
			_bytecode.Emit(Op.SetIndex);
			var jmpLoc = _bytecode.EmitNumberOperandPlaceholder();
			var functionName = node.Public ? "public." : "";
			if (!string.IsNullOrEmpty(node.ObjectName)) functionName += node.ObjectName + ".";
			functionName += node.Name;
			if (node.ObjectName == "universe") functionName = (node.Public ? "public." : "") + node.Name + "," + functionName;
			var addedFunction = _bytecode.AddFunction(functionName, _bytecode.OpIndex, 0);
			_bytecode.Emit(Op.TypeArray);
			for (var i = node.Args.Count - 1; i >= 0; --i) Emit(node.Args[i]);
			_bytecode.Emit(Op.FuncParamsEnd);
			_bytecode.Emit(Op.Jmp);
			if (node.Body.Exists(ContainsCall)) _bytecode.Emit(Op.CmdCall);
			foreach (var statement in node.Body) EmitStatement(statement);
			var lastStatement = LastMeaningfulStatement(node.Body);
			if (node.Name != "getPreviewSpriteCount" && (lastStatement == null || !DefinitelyReturns(lastStatement)))
			{
				_bytecode.Emit(Op.TypeNumber);
				_bytecode.EmitDynamicNumber(0);
				_bytecode.Emit(Op.Ret);
			}
			if (patchToFinal)
			{
				if (addedFunction) _bytecode.AddPrejumpPatch(jmpLoc);
			}
			else _bytecode.PatchShort(jmpLoc, _bytecode.OpIndex);
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
			_ => false
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
				else if (exprStatement.Expression is CallExpr or MethodCallExpr or DynamicMethodCallExpr) _bytecode.Emit(Op.IndexDec);
			}
			else if (statement is ReturnStmt returnStatement)
			{
				Emit(returnStatement.Expression);
				_bytecode.Emit(Op.Ret);
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
			if (!IsBooleanExpr(statement.Condition) && NeedsNumericConversion(statement.Condition)) _bytecode.Emit(Op.ConvToFloat);
			foreach (var patch in conditionSuccessPatches) _bytecode.PatchShort(patch, _bytecode.OpIndex);
			_bytecode.Emit(Op.If);
			var failLoc = _bytecode.EmitNumberOperandPlaceholder();
			foreach (var stmt in statement.ThenBody) EmitStatement(stmt);
			var failTarget = _bytecode.OpIndex + (statement.HasElse ? 1 : 0);
			_bytecode.PatchShort(failLoc, failTarget);
			foreach (var patch in conditionFailPatches) _bytecode.PatchShort(patch, failTarget);
			if (statement.HasElse)
			{
				_bytecode.Emit(Op.SetIndex);
				var exitLoc = _bytecode.EmitNumberOperandPlaceholder();
				foreach (var stmt in statement.ElseBody) EmitStatement(stmt);
				_bytecode.PatchShort(exitLoc, _bytecode.OpIndex);
			}
		}

		private void EmitFor(ForStmt statement)
		{
			if (statement.Init != null) EmitUnusedExpression(statement.Init);
			var start = _bytecode.OpIndex;
			List<int> conditionFailPatches = [];
			List<int> conditionSuccessPatches = [];
			_conditionFailPatches.Push(conditionFailPatches);
			_conditionSuccessPatches.Push(conditionSuccessPatches);
			if (UsesInlineForCondition(statement.Condition)) Emit(statement.Condition);
			else Emit(statement.Condition, false, false, 0, true);
			_conditionSuccessPatches.Pop();
			_conditionFailPatches.Pop();
			if (!IsBooleanExpr(statement.Condition) && NeedsNumericConversion(statement.Condition)) _bytecode.Emit(Op.ConvToFloat);
			foreach (var patch in conditionSuccessPatches) _bytecode.PatchShort(patch, _bytecode.OpIndex);
			_bytecode.Emit(Op.If);
			var breakLoc = _bytecode.EmitNumberOperandPlaceholder();
			List<int> breakPatches = [];
			_breakPatches.Push(breakPatches);
			List<int> continuePatches = [];
			_continuePatches.Push(continuePatches);
			_bytecode.Emit(Op.CmdCall);
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			foreach (var patch in continuePatches) _bytecode.PatchShort(patch, _bytecode.OpIndex);
			if (statement.Post != null) EmitUnusedExpression(statement.Post);
			_bytecode.Emit(Op.SetIndex);
			_bytecode.EmitDynamicNumber(start);
			_continuePatches.Pop();
			_breakPatches.Pop();
			_bytecode.PatchShort(breakLoc, _bytecode.OpIndex);
			foreach (var patch in breakPatches) _bytecode.PatchShort(patch, _bytecode.OpIndex);
			foreach (var patch in conditionFailPatches) _bytecode.PatchShort(patch, _bytecode.OpIndex);
		}

		private void EmitForEach(ForEachStmt statement)
		{
			Emit(statement.Name);
			Emit(statement.Source);
			_bytecode.Emit(Op.ConvToObject);
			_bytecode.Emit(Op.TypeNumber);
			_bytecode.EmitDynamicNumber(0);
			var start = _bytecode.OpIndex;
			_bytecode.Emit(Op.Foreach);
			var breakLoc = _bytecode.EmitNumberOperandPlaceholder();
			List<int> breakPatches = [];
			_breakPatches.Push(breakPatches);
			List<int> continuePatches = [];
			_continuePatches.Push(continuePatches);
			_bytecode.Emit(Op.CmdCall);
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			foreach (var patch in continuePatches) _bytecode.PatchShort(patch, _bytecode.OpIndex);
			_bytecode.Emit(Op.Inc);
			_bytecode.Emit(Op.SetIndex);
			_bytecode.EmitDynamicNumber(start);
			_continuePatches.Pop();
			_breakPatches.Pop();
			_bytecode.PatchShort(breakLoc, _bytecode.OpIndex);
			foreach (var patch in breakPatches) _bytecode.PatchShort(patch, _bytecode.OpIndex);
			_bytecode.Emit(Op.IndexDec);
		}

		private void EmitWhile(WhileStmt statement)
		{
			var start = _bytecode.OpIndex;
			List<int> conditionFailPatches = [];
			List<int> conditionSuccessPatches = [];
			_conditionFailPatches.Push(conditionFailPatches);
			_conditionSuccessPatches.Push(conditionSuccessPatches);
			Emit(statement.Condition, false, false, 0, true);
			_conditionSuccessPatches.Pop();
			_conditionFailPatches.Pop();
			if (!IsBooleanExpr(statement.Condition) && NeedsNumericConversion(statement.Condition)) _bytecode.Emit(Op.ConvToFloat);
			foreach (var patch in conditionSuccessPatches) _bytecode.PatchShort(patch, _bytecode.OpIndex);
			List<int> breakPatches = [];
			_breakPatches.Push(breakPatches);
			List<int> continuePatches = [];
			_continuePatches.Push(continuePatches);
			_bytecode.Emit(Op.If);
			var breakLoc = _bytecode.EmitNumberOperandPlaceholder();
			breakPatches.Add(breakLoc);
			_bytecode.Emit(Op.CmdCall);
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			foreach (var patch in continuePatches) _bytecode.PatchShort(patch, _bytecode.OpIndex);
			_bytecode.Emit(Op.SetIndex);
			_bytecode.EmitNumberOperand(start);
			_continuePatches.Pop();
			_breakPatches.Pop();
			foreach (var patch in breakPatches) _bytecode.PatchShort(patch, _bytecode.OpIndex);
			foreach (var patch in conditionFailPatches) _bytecode.PatchShort(patch, _bytecode.OpIndex);
		}

		private void EmitWith(WithStmt statement)
		{
			Emit(statement.Target);
			_bytecode.Emit(Op.ConvToObject);
			_bytecode.Emit(Op.With);
			var exitLoc = _bytecode.EmitNumberOperandPlaceholder();
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			_bytecode.Emit(Op.WithEnd);
			_bytecode.PatchShort(exitLoc, _bytecode.OpIndex);
		}

		private void EmitSwitch(SwitchStmt statement)
		{
			_bytecode.Emit(Op.SetIndex);
			var caseTestLoc = _bytecode.EmitNumberOperandPlaceholder();
			List<int> caseStarts = [];
			List<int> breakPatches = [];
			_breakPatches.Push(breakPatches);
			foreach (var switchCase in statement.Cases)
			{
				var start = _bytecode.OpIndex;
				foreach (var _ in switchCase.Labels) caseStarts.Add(start);
				List<int> continuePatches = [];
				_continuePatches.Push(continuePatches);
				foreach (var stmt in switchCase.Body) EmitStatement(stmt);
				_continuePatches.Pop();
				foreach (var patch in continuePatches) _bytecode.PatchShort(patch, start);
			}
			_breakPatches.Pop();
			_bytecode.PatchShort(caseTestLoc, _bytecode.OpIndex);
			Emit(statement.Expression);
			var caseIndex = 0;
			foreach (var switchCase in statement.Cases)
			foreach (var label in switchCase.Labels)
			{
				if (label != null)
				{
					_bytecode.Emit(Op.CopyLastOp);
					Emit(label);
					_bytecode.Emit(Op.Eq);
					_bytecode.Emit(Op.SetIndexTrue);
				}
				else _bytecode.Emit(Op.SetIndex);
				_bytecode.EmitDynamicNumber(caseStarts[caseIndex++]);
			}
			foreach (var patch in breakPatches) _bytecode.PatchShort(patch, _bytecode.OpIndex);
			_bytecode.Emit(Op.IndexDec);
		}

		private void EmitBreak()
		{
			if (_breakPatches.Count == 0) return;
			_bytecode.Emit(Op.SetIndex);
			_breakPatches.Peek().Add(_bytecode.EmitNumberOperandPlaceholder());
		}

		private void EmitContinue()
		{
			if (_continuePatches.Count == 0) return;
			_bytecode.Emit(Op.SetIndex);
			_continuePatches.Peek().Add(_bytecode.EmitNumberOperandPlaceholder());
		}

		private void EmitNewStatement(NewStmt statement)
		{
			foreach (var arg in statement.Args) Emit(arg);
			_bytecode.Emit(Op.InlineNew);
			_bytecode.Emit(Op.CopyLastOp);
			_bytecode.Emit(Op.CopyLastOp);
			_bytecode.Emit(Op.CopyLastOp);
			_bytecode.Emit(Op.TypeString);
			_bytecode.EmitDynamicStringIndex(_bytecode.GetString(statement.TypeName));
			_bytecode.Emit(Op.ConvToString);
			_bytecode.Emit(Op.NewObject);
			_bytecode.Emit(Op.Assign);
			_bytecode.Emit(Op.ConvToObject);
			_bytecode.Emit(Op.With);
			var withLoc = _bytecode.EmitNumberOperandPlaceholder();
			var previousCount = _newObjectCount++;
			foreach (var stmt in statement.Body) EmitStatement(stmt);
			_bytecode.Emit(Op.WithEnd);
			_bytecode.PatchShort(withLoc, _bytecode.OpIndex);
			for (var i = 0; i < _newObjectCount - previousCount; ++i)
			{
				_bytecode.Emit(Op.TypeArray);
				_bytecode.Emit(Op.SwapLastOps);
				_bytecode.Emit(Op.TypeVar);
				_bytecode.EmitDynamicStringIndex(_bytecode.GetString("addcontrol"));
				_bytecode.Emit(Op.Call);
				_bytecode.Emit(Op.IndexDec);
			}
			_newObjectCount--;
		}

		private void Emit(Expr expr, bool copyAssignmentTarget, bool logicalInline, int suppressedLogicalPatchOffset, bool controlLogical = false, bool negatedLogical = false, bool preservePostfixValue = true, bool leftNegatedControlAnd = false)
		{
			switch (expr)
			{
				case BinaryExpr { Op: "=", Left: MultiArrayIndexExpr left, Right: var right }:
					EmitMultiArrayIndex(left, true);
					if (copyAssignmentTarget) _bytecode.Emit(Op.CopyLastOp);
					if (right is BinaryExpr { Op: "=" }) Emit(right, true, true, 0);
					else Emit(right);
					_bytecode.Emit(Op.ArrayMultiDimAssign);
					break;
				case BinaryExpr { Op: "=", Left: ArrayIndexExpr left, Right: var right }:
					EmitArrayIndex(left, true);
					if (copyAssignmentTarget) _bytecode.Emit(Op.CopyLastOp);
					if (right is BinaryExpr { Op: "=" }) Emit(right, true, true, 0);
					else Emit(right);
					_bytecode.Emit(Op.ArrayAssign);
					break;
				case BinaryExpr { Op: "=", Left: var left, Right: var right }:
					Emit(left);
					if (copyAssignmentTarget) _bytecode.Emit(Op.CopyLastOp);
					if (right is BinaryExpr { Op: "=" }) Emit(right, true, true, 0);
					else Emit(right);
					_bytecode.Emit(Op.Assign);
					break;
				case BinaryExpr { Op: var op, Left: MultiArrayIndexExpr left, Right: var right } when IsCompoundAssign(op):
					EmitMultiArrayIndex(left, true);
					_bytecode.Emit(Op.CopyLastOp);
					_bytecode.Emit(op == "@=" ? Op.ConvToString : Op.ConvToFloat);
					Emit(right);
					if (op == "@=" && NeedsStringConversion(right)) _bytecode.Emit(Op.ConvToString);
					else if (op != "@=" && NeedsNumericConversion(right)) _bytecode.Emit(Op.ConvToFloat);
					_bytecode.Emit(CompoundOpcode(op));
					_bytecode.Emit(Op.ArrayMultiDimAssign);
					break;
				case BinaryExpr { Op: var op, Left: ArrayIndexExpr left, Right: var right } when IsCompoundAssign(op):
					EmitArrayIndex(left, true);
					_bytecode.Emit(Op.CopyLastOp);
					_bytecode.Emit(op == "@=" ? Op.ConvToString : Op.ConvToFloat);
					Emit(right);
					if (op == "@=" && NeedsStringConversion(right)) _bytecode.Emit(Op.ConvToString);
					else if (op != "@=" && NeedsNumericConversion(right)) _bytecode.Emit(Op.ConvToFloat);
					_bytecode.Emit(CompoundOpcode(op));
					_bytecode.Emit(Op.ArrayAssign);
					break;
				case BinaryExpr { Op: var op, Left: var left, Right: var right } when IsCompoundAssign(op):
					Emit(left);
					_bytecode.Emit(Op.CopyLastOp);
					_bytecode.Emit(op == "@=" ? Op.ConvToString : Op.ConvToFloat);
					Emit(right);
					if (op == "@=" && NeedsStringConversion(right)) _bytecode.Emit(Op.ConvToString);
					else if (op != "@=" && NeedsNumericConversion(right)) _bytecode.Emit(Op.ConvToFloat);
					_bytecode.Emit(CompoundOpcode(op));
					_bytecode.Emit(Op.Assign);
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
					if (!IsBooleanExpr(logical.Left)) _bytecode.Emit(Op.ConvToFloat);
					if (localSuccessPatches != null)
					{
						for (var i = localSuccessStart; i < localSuccessPatches.Count; ++i) _bytecode.PatchShort(localSuccessPatches[i], _bytecode.OpIndex);
						localSuccessPatches.RemoveRange(localSuccessStart, localSuccessPatches.Count - localSuccessStart);
					}
					if (localFailPatches != null)
					{
						for (var i = localFailStart; i < localFailPatches.Count; ++i) _bytecode.PatchShort(localFailPatches[i], _bytecode.OpIndex + 1);
						localFailPatches.RemoveRange(localFailStart, localFailPatches.Count - localFailStart);
					}
					_bytecode.Emit(logical.Op == "&&" && controlLogical ? Op.If : logical.Op == "&&" ? Op.And : Op.Or);
					var loc = _bytecode.EmitNumberOperandPlaceholder();
					Emit(logical.Right, false, false, 0, controlLogical, negatedLogical);
					if (!IsBooleanExpr(logical.Right)) _bytecode.Emit(Op.ConvToFloat);
					if (logicalInline) _bytecode.Emit(Op.InlineConditional);
					if (logical.Op == "&&" && controlLogical && _conditionFailPatches.Count > 0) _conditionFailPatches.Peek().Add(loc);
					else if (logical.Op == "||" && controlLogical && _conditionSuccessPatches.Count > 0) _conditionSuccessPatches.Peek().Add(loc);
					else _bytecode.PatchShort(loc, _bytecode.OpIndex - (logicalInline ? 1 : 0) + (!logicalInline ? suppressedLogicalPatchOffset : 0));
					break;
				case TernaryExpr ternary:
					EmitTernary(ternary);
					break;
				case BinaryExpr { Op: "@", Left: var left, Right: var right } when _dynamicMethodName:
					Emit(left);
					if (left is StringExpr || NeedsStringConversion(left)) _bytecode.Emit(Op.ConvToString);
					Emit(right);
					if (right is not StringExpr) _bytecode.Emit(Op.ConvToString);
					_bytecode.Emit(Op.Join);
					break;
				case BinaryExpr { Op: " " or "\n" or "\t", Left: var left, Right: var right } spacedConcat:
					Emit(left);
					if (NeedsStringConversion(left)) _bytecode.Emit(Op.ConvToString);
					_bytecode.Emit(Op.TypeString);
					_bytecode.EmitDynamicStringIndex(_bytecode.GetString(spacedConcat.Op));
					_bytecode.Emit(Op.Join);
					Emit(right);
					if (NeedsStringConversion(right)) _bytecode.Emit(Op.ConvToString);
					_bytecode.Emit(Op.Join);
					break;
				case InExpr inExpr:
					Emit(inExpr.Expression);
					Emit(inExpr.Lower);
					if (inExpr.Upper != null)
					{
						if (!IsNumberExpr(inExpr.Lower)) _bytecode.Emit(Op.ConvToFloat);
						Emit(inExpr.Upper);
						if (!IsNumberExpr(inExpr.Upper)) _bytecode.Emit(Op.ConvToFloat);
						_bytecode.Emit(Op.InRange);
					}
					else
					{
						_bytecode.Emit(Op.ConvToObject);
						_bytecode.Emit(Op.InObj);
					}
					break;
				case BinaryExpr { Op: "*", Left: UnaryExpr { Op: "-", Expression: var negatedLeft }, Right: var right }:
					Emit(negatedLeft);
					if (NeedsNumericConversion(negatedLeft)) _bytecode.Emit(Op.ConvToFloat);
					Emit(right);
					if (NeedsNumericConversion(right)) _bytecode.Emit(Op.ConvToFloat);
					_bytecode.Emit(Op.Mul);
					_bytecode.Emit(Op.UnarySub);
					break;
				case BinaryExpr { Op: "==" or "!=", Left: StringCastExpr { Expression: MemberExpr left }, Right: StringCastExpr { Expression: MemberExpr right } } castCompare:
					Emit(left);
					Emit(right);
					_bytecode.Emit(Op.ConvToString);
					_bytecode.Emit(BinaryOpcode(castCompare.Op));
					_bytecode.Emit(Op.ConvToString);
					break;
				case BinaryExpr { Op: "==" or "!=", Left: StringCastExpr { Expression: MemberExpr left }, Right: StringCastExpr { Expression: ArrayLiteralExpr right } } arrayCastCompare:
					Emit(left);
					Emit(new StringCastExpr(right));
					_bytecode.Emit(BinaryOpcode(arrayCastCompare.Op));
					_bytecode.Emit(Op.ConvToString);
					break;
				case BinaryExpr { Op: "==" or "!=", Left: StringCastExpr { Expression: ArrayLiteralExpr left }, Right: StringCastExpr { Expression: ArrayLiteralExpr right } } arrayPairCastCompare:
					Emit(left);
					Emit(new StringCastExpr(right));
					_bytecode.Emit(BinaryOpcode(arrayPairCastCompare.Op));
					_bytecode.Emit(Op.ConvToString);
					break;
				case BinaryExpr binary:
					Emit(binary.Left);
					if (binary.Op == "@" && NeedsStringConversion(binary.Left)) _bytecode.Emit(Op.ConvToString);
					if ((IsNumericOp(binary.Op) || IsComparisonOp(binary.Op)) && NeedsNumericConversion(binary.Left)) _bytecode.Emit(Op.ConvToFloat);
					if (IsComparisonOp(binary.Op) && binary.Left is UnaryExpr { Op: "++" or "--", Postfix: true }) _bytecode.Emit(Op.ConvToFloat);
					if (binary.Op == "%" && binary.Left is BinaryExpr { Op: "+=" or "-=" or "*=" or "/=" or "%=" }) _bytecode.Emit(Op.ConvToFloat);
					if (binary.Op == "&" && binary.Left is not NumberExpr) _bytecode.Emit(Op.ConvToFloat);
					if (binary.Op == "@" && binary.Left is StringCastExpr && binary.Right is TernaryExpr) _bytecode.Emit(Op.ConvToString);
					if (IsNumericOp(binary.Op) && UsesPreviewSpriteTernary(binary.Left)) _bytecode.Emit(Op.ConvToFloat);
					Emit(binary.Right);
					if ((IsNumericOp(binary.Op) || IsComparisonOp(binary.Op)) && NeedsNumericConversion(binary.Right)) _bytecode.Emit(Op.ConvToFloat);
					if (IsNumericOp(binary.Op) && UsesSignTernary(binary.Right)) _bytecode.Emit(Op.ConvToFloat);
					if (binary.Op == "@" && (_dynamicMethodName || NeedsStringConversion(binary.Right))) _bytecode.Emit(Op.ConvToString);
					_bytecode.Emit(BinaryOpcode(binary.Op));
					break;
				case MemberExpr member:
					Emit(member.Object);
					if (NeedsObjectConversion(member.Object)) _bytecode.Emit(Op.ConvToObject);
					_bytecode.Emit(Op.TypeVar);
					_bytecode.EmitDynamicStringIndex(_bytecode.GetString(member.Name));
					_bytecode.Emit(Op.MemberAccess);
					break;
				case DynamicMemberExpr member:
					Emit(member.Object);
					if (NeedsObjectConversion(member.Object)) _bytecode.Emit(Op.ConvToObject);
					if (member.Name is StringCastExpr { Expression: IdentifierExpr or MemberExpr { Object: IdentifierExpr { Name: "temp" } } } nameCast)
					{
						Emit(nameCast.Expression);
						if (NeedsNumericConversion(nameCast.Expression)) _bytecode.Emit(Op.ConvToFloat);
						_bytecode.Emit(Op.Add);
					}
					else
					{
						Emit(member.Name);
						if (NeedsStringConversion(member.Name)) _bytecode.Emit(Op.ConvToString);
					}
					_bytecode.Emit(Op.MemberAccess);
					break;
				case StringCastExpr cast:
					Emit(cast.Expression);
					_bytecode.Emit(Op.ConvToString);
					if (ExpressionTypeOf(cast.Expression) == ExprType.Array) _bytecode.Emit(Op.MemberAccess);
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
				case IdentifierExpr id when _constants.TryGetValue(id.Name, out var constant):
					Emit(constant);
					break;
				case IdentifierExpr { Name: "this" }:
					_bytecode.Emit(Op.This);
					break;
				case IdentifierExpr { Name: "temp" }:
					_bytecode.Emit(Op.Temp);
					break;
				case IdentifierExpr { Name: "thiso" }:
					_bytecode.Emit(Op.Thiso);
					break;
				case IdentifierExpr { Name: "player" }:
					_bytecode.Emit(Op.Player);
					break;
				case IdentifierExpr { Name: "playero" }:
					_bytecode.Emit(Op.Playero);
					break;
				case IdentifierExpr { Name: "level" }:
					_bytecode.Emit(Op.Level);
					break;
				case IdentifierExpr { Name: "pi" }:
					_bytecode.Emit(Op.Pi);
					break;
				case IdentifierExpr id:
					_bytecode.Emit(Op.TypeVar);
					_bytecode.EmitDynamicStringIndex(_bytecode.GetString(id.Name));
					break;
				case EnumExpr enumExpr:
					_bytecode.Emit(Op.TypeNumber);
					_bytecode.EmitDynamicNumber(_enums.TryGetValue(enumExpr.EnumName, out var members) && members.TryGetValue(enumExpr.MemberName, out var value) ? value : 0);
					break;
				case NumberExpr number:
					_bytecode.Emit(Op.TypeNumber);
					if (number.Text.Contains('.', StringComparison.Ordinal)) _bytecode.EmitDoubleNumber(CurrentFloatText(number.Text));
					else _bytecode.EmitDynamicNumber(int.Parse(number.Text, CultureInfo.InvariantCulture));
					break;
				case StringExpr str:
					_bytecode.Emit(Op.TypeString);
					_bytecode.EmitDynamicStringIndex(_bytecode.GetString(str.Value));
					break;
				case BoolExpr boolean:
					_bytecode.Emit(boolean.Value ? Op.TypeTrue : Op.TypeFalse);
					break;
				case NullExpr:
					_bytecode.Emit(Op.TypeNull);
					break;
				case UnaryExpr { Op: "-", Expression: NumberExpr number }:
					_bytecode.Emit(Op.TypeNumber);
					if (number.Text.Contains('.', StringComparison.Ordinal)) _bytecode.EmitDoubleNumber(NextNegativeFloatText(number.Text));
					else _bytecode.EmitDynamicNumber(-int.Parse(number.Text, CultureInfo.InvariantCulture));
					break;
				case UnaryExpr { Op: "-", Expression: var unaryValue }:
					Emit(unaryValue);
					_bytecode.Emit(Op.ConvToFloat);
					_bytecode.Emit(Op.UnarySub);
					break;
				case UnaryExpr { Op: "!", Expression: var notValue }:
					Emit(notValue, false, false, 0, leftNegatedControlAnd, true);
					if (!IsBooleanExpr(notValue)) _bytecode.Emit(Op.ConvToFloat);
					_bytecode.Emit(Op.Not);
					break;
				case UnaryExpr { Op: "~", Expression: var invertValue }:
					Emit(invertValue);
					if (!IsNumberExpr(invertValue)) _bytecode.Emit(Op.ConvToFloat);
					_bytecode.Emit(Op.BitInvert);
					break;
				case UnaryExpr { Op: "++", Expression: var incValue, Postfix: true }:
					Emit(incValue);
					if (preservePostfixValue)
					{
						_bytecode.Emit(Op.CopyLastOp);
						_bytecode.Emit(Op.ConvToFloat);
						_bytecode.Emit(Op.SwapLastOps);
					}
					_bytecode.Emit(Op.Inc);
					_bytecode.Emit(Op.IndexDec);
					break;
				case UnaryExpr { Op: "--", Expression: var decValue, Postfix: true }:
					Emit(decValue);
					if (preservePostfixValue)
					{
						_bytecode.Emit(Op.CopyLastOp);
						_bytecode.Emit(Op.ConvToFloat);
						_bytecode.Emit(Op.SwapLastOps);
					}
					_bytecode.Emit(Op.Dec);
					_bytecode.Emit(Op.IndexDec);
					break;
				case UnaryExpr { Op: "++", Expression: var incValue }:
					Emit(incValue);
					_bytecode.Emit(Op.Inc);
					if (!preservePostfixValue) _bytecode.Emit(Op.IndexDec);
					break;
				case UnaryExpr { Op: "--", Expression: var decValue }:
					Emit(decValue);
					_bytecode.Emit(Op.Dec);
					if (!preservePostfixValue) _bytecode.Emit(Op.IndexDec);
					break;
				case CastExpr { Type: "int", Expression: var castValue }:
					Emit(castValue);
					if (!IsNumberExpr(castValue)) _bytecode.Emit(Op.ConvToFloat);
					_bytecode.Emit(Op.Int);
					break;
				case CastExpr { Type: "float", Expression: var castValue }:
					Emit(castValue);
					if (!IsNumberExpr(castValue)) _bytecode.Emit(Op.ConvToFloat);
					break;
				case CastExpr { Type: "_", Expression: var castValue }:
					Emit(castValue);
					if (castValue is not StringExpr) _bytecode.Emit(Op.ConvToString);
					_bytecode.Emit(Op.Translate);
					break;
				case CallExpr call when BuiltInCalls.TryGetValue(call.Name, out var builtIn):
					for (var i = call.Args.Count - 1; i >= 0; --i)
					{
						Emit(call.Args[i]);
						if (!IsNumberExpr(call.Args[i])) _bytecode.Emit(Op.ConvToFloat);
					}
					_bytecode.Emit(builtIn);
					break;
				case CallExpr call when NonReversedBuiltInCalls.TryGetValue(call.Name, out var builtIn):
					for (var i = 0; i < call.Args.Count; ++i)
					{
						Emit(call.Args[i]);
						if (!IsNumberExpr(call.Args[i])) _bytecode.Emit(Op.ConvToFloat);
					}
					_bytecode.Emit(builtIn);
					break;
				case CallExpr { Name: "arraylen" or "sarraylen" } call:
					for (var i = call.Args.Count - 1; i >= 0; --i)
					{
						Emit(call.Args[i]);
						if (NeedsObjectConversion(call.Args[i])) _bytecode.Emit(Op.ConvToObject);
					}
					_bytecode.Emit(Op.ObjSize);
					break;
				case CallExpr { Name: "waitfor" } call:
					for (var i = 0; i < call.Args.Count; ++i)
					{
						Emit(call.Args[i]);
						if (i < 2 && NeedsStringConversion(call.Args[i])) _bytecode.Emit(Op.ConvToString);
						else if (i == 2 && !IsNumberExpr(call.Args[i])) _bytecode.Emit(Op.ConvToFloat);
					}
					_bytecode.Emit(Op.WaitFor);
					break;
				case CallExpr { Name: "sleep" } call:
					for (var i = 0; i < call.Args.Count; ++i)
					{
						Emit(call.Args[i]);
						if (!IsNumberExpr(call.Args[i])) _bytecode.Emit(Op.ConvToFloat);
					}
					_bytecode.Emit(Op.Sleep);
					break;
				case CallExpr { Name: "setarray" } call:
					if (call.Args.Count > 0)
					{
						Emit(call.Args[0]);
						if (NeedsObjectConversion(call.Args[0])) _bytecode.Emit(Op.ConvToObject);
					}
					if (call.Args.Count > 1)
					{
						Emit(call.Args[1]);
						if (!IsNumberExpr(call.Args[1])) _bytecode.Emit(Op.ConvToFloat);
					}
					_bytecode.Emit(Op.SetArray);
					break;
				case CallExpr { Name: "makevar" } call:
					for (var i = call.Args.Count - 1; i >= 0; --i)
					{
						Emit(call.Args[i]);
						_bytecode.Emit(Op.ConvToString);
					}
					_bytecode.Emit(Op.MakeVar);
					break;
				case CallExpr { Name: "format" } call:
					_bytecode.Emit(Op.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					_bytecode.Emit(Op.Format);
					break;
				case CallExpr call:
					_bytecode.Emit(Op.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					_bytecode.Emit(Op.TypeVar);
					_bytecode.EmitDynamicStringIndex(_bytecode.GetString(call.Name));
					_bytecode.Emit(Op.Call);
					break;
				case ChainedCallExpr call:
					_bytecode.Emit(Op.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					Emit(call.Call);
					_bytecode.Emit(Op.Call);
					break;
				case DynamicMethodCallExpr call:
					_bytecode.Emit(Op.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					Emit(call.Object);
					if (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr) _bytecode.Emit(Op.ConvToObject);
					_dynamicMethodName = true;
					if (call.Name is StringCastExpr { Expression: IdentifierExpr or MemberExpr { Object: IdentifierExpr { Name: "temp" } } } callNameCast)
					{
						Emit(callNameCast.Expression);
						if (NeedsNumericConversion(callNameCast.Expression)) _bytecode.Emit(Op.ConvToFloat);
						_bytecode.Emit(Op.Add);
					}
					else if (call.Name is StringCastExpr { Expression: BinaryExpr { Op: "@" } nameConcat }) Emit(nameConcat);
					else Emit(call.Name);
					_dynamicMethodName = false;
					if (NeedsStringConversion(call.Name) && call.Name is not BinaryExpr { Op: "@" }) _bytecode.Emit(Op.ConvToString);
					_bytecode.Emit(Op.MemberAccess);
					_bytecode.Emit(Op.Call);
					break;
				case MethodCallExpr call:
					if (call.Name == "size" && call.Args.Count == 0)
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr) _bytecode.Emit(Op.ConvToObject);
						_bytecode.Emit(Op.ObjSize);
						break;
					}
					if (call.Name == "type" && call.Args.Count == 0)
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object)) _bytecode.Emit(Op.ConvToObject);
						_bytecode.Emit(Op.ObjType);
						break;
					}
					if (call.Name == "indices" && call.Args.Count == 0)
					{
						Emit(call.Object);
						_bytecode.Emit(Op.ObjIndices);
						break;
					}
					if (call.Name == "link" && call.Args.Count == 0)
					{
						Emit(call.Object);
						_bytecode.Emit(Op.ObjLink);
						break;
					}
					if (call.Name == "index")
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr) _bytecode.Emit(Op.ConvToObject);
						foreach (var arg in call.Args) Emit(arg);
						_bytecode.Emit(Op.ObjIndex);
						break;
					}
					if (call.Name == "length" && call.Args.Count == 0)
					{
						Emit(call.Object);
						_bytecode.Emit(Op.ConvToString);
						_bytecode.Emit(Op.ObjLength);
						break;
					}
					if (call.Name == "substring")
					{
						Emit(call.Object);
						_bytecode.Emit(Op.ConvToString);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (!IsNumberExpr(arg)) _bytecode.Emit(Op.ConvToFloat);
						}
						_bytecode.Emit(Op.ObjSubstr);
						break;
					}
					if (call.Name == "pos")
					{
						Emit(call.Object);
						_bytecode.Emit(Op.ConvToString);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (NeedsStringConversion(arg)) _bytecode.Emit(Op.ConvToString);
						}
						_bytecode.Emit(Op.ObjPos);
						break;
					}
					if (call.Name == "charat")
					{
						Emit(call.Object);
						_bytecode.Emit(Op.ConvToString);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (!IsNumberExpr(arg)) _bytecode.Emit(Op.ConvToFloat);
						}
						_bytecode.Emit(Op.ObjCharAt);
						break;
					}
					if (call.Name is "starts" or "ends")
					{
						Emit(call.Object);
						_bytecode.Emit(Op.ConvToString);
						foreach (var arg in call.Args) Emit(arg);
						_bytecode.Emit(call.Name == "starts" ? Op.ObjStarts : Op.ObjEnds);
						break;
					}
					if (call.Name == "trim" && call.Args.Count == 0)
					{
						Emit(call.Object);
						_bytecode.Emit(Op.ConvToString);
						_bytecode.Emit(Op.ObjTrim);
						break;
					}
					if (call.Name == "tokenize")
					{
						Emit(call.Object);
						_bytecode.Emit(Op.ConvToString);
						if (call.Args.Count == 0)
						{
							_bytecode.Emit(Op.TypeString);
							_bytecode.EmitDynamicStringIndex(_bytecode.GetString(" ,"));
						}
						else foreach (var arg in call.Args) Emit(arg);
						_bytecode.Emit(Op.ObjTokenize);
						break;
					}
					if (call.Name == "positions")
					{
						Emit(call.Object);
						_bytecode.Emit(Op.ConvToString);
						foreach (var arg in call.Args)
						{
							Emit(arg);
							if (NeedsStringConversion(arg)) _bytecode.Emit(Op.ConvToString);
						}
						_bytecode.Emit(Op.ObjPositions);
						break;
					}
					if (call.Name == "subarray")
					{
						for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
						Emit(call.Object);
						_bytecode.Emit(Op.ObjSubarray);
						break;
					}
					if (call.Name == "clear" && call.Args.Count == 0)
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object)) _bytecode.Emit(Op.ConvToObject);
						_bytecode.Emit(Op.ObjClear);
						break;
					}
					if (call.Name is "add" or "delete")
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr) _bytecode.Emit(Op.ConvToObject);
						foreach (var arg in call.Args) Emit(arg);
						_bytecode.Emit(call.Name == "add" ? Op.ObjAddString : Op.ObjDeleteString);
						break;
					}
					if (call.Name is "insert" or "remove" or "replace")
					{
						Emit(call.Object);
						if (NeedsObjectConversion(call.Object)) _bytecode.Emit(Op.ConvToObject);
						for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
						_bytecode.Emit(call.Name switch
						{
							"insert" => Op.ObjInsertString,
							"remove" => Op.ObjRemoveString,
							_ => Op.ObjReplaceString
						});
						break;
					}
					_bytecode.Emit(Op.TypeArray);
					for (var i = call.Args.Count - 1; i >= 0; --i) Emit(call.Args[i]);
					Emit(call.Object);
					if (NeedsObjectConversion(call.Object) || call.Object is ArrayIndexExpr) _bytecode.Emit(Op.ConvToObject);
					_bytecode.Emit(Op.TypeVar);
					_bytecode.EmitDynamicStringIndex(_bytecode.GetString(call.Name));
					_bytecode.Emit(Op.MemberAccess);
					_bytecode.Emit(Op.Call);
					break;
				case NewObjectExpr obj:
					var typeNameIndex = _bytecode.GetString(obj.TypeName);
					if (obj.Args.Count == 1)
					{
						Emit(obj.Args[0]);
						_bytecode.Emit(Op.InlineNew);
					}
					else
					{
						_bytecode.Emit(Op.TypeVar);
						_bytecode.EmitDynamicStringIndex(_bytecode.GetString("unknown_object"));
					}
					_bytecode.Emit(Op.TypeString);
					_bytecode.EmitDynamicStringIndex(typeNameIndex);
					_bytecode.Emit(Op.NewObject);
					break;
				case NewArrayExpr array:
					for (var i = 0; i < array.Dimensions.Count; ++i)
					{
						Emit(array.Dimensions[i]);
						if (!IsNumberExpr(array.Dimensions[i])) _bytecode.Emit(Op.ConvToFloat);
						_bytecode.Emit(i == 0 ? Op.ArrayNew : Op.ArrayNewMultiDim);
					}
					break;
				case LambdaExpr lambda:
					EmitFunction(new(lambda.Name, null, true, lambda.Args, lambda.Body), false);
					_bytecode.Emit(Op.This);
					_bytecode.Emit(Op.TypeVar);
					_bytecode.EmitDynamicStringIndex(_bytecode.GetString(lambda.Name));
					_bytecode.Emit(Op.MemberAccess);
					_bytecode.Emit(Op.ConvToObject);
					break;
				case ArrayLiteralExpr array:
					_bytecode.Emit(Op.TypeArray);
					for (var i = array.Values.Count - 1; i >= 0; --i) Emit(array.Values[i]);
					_bytecode.Emit(Op.ArrayEnd);
					break;
			}
		}

		private void EmitArrayIndex(ArrayIndexExpr expr, bool assignmentTarget)
		{
			Emit(expr.Target);
			if (expr.Target is not ArrayIndexExpr) _bytecode.Emit(Op.ConvToObject);
			Emit(expr.Index);
			if (!IsNumberExpr(expr.Index)) _bytecode.Emit(Op.ConvToFloat);
			if (!assignmentTarget) _bytecode.Emit(Op.Array);
		}

		private void EmitTernary(TernaryExpr ternary, int? chainedSuccessLoc = null)
		{
			Emit(ternary.Condition, false, false, 0);
			if (!IsBooleanExpr(ternary.Condition)) _bytecode.Emit(Op.ConvToFloat);
			if (chainedSuccessLoc.HasValue) _bytecode.PatchShort(chainedSuccessLoc.Value, _bytecode.OpIndex);
			_bytecode.Emit(Op.If);
			var failLoc = _bytecode.EmitNumberOperandPlaceholder();
			Emit(ternary.WhenTrue);
			_bytecode.PatchShort(failLoc, _bytecode.OpIndex + 1);
			_bytecode.Emit(Op.SetIndex);
			var successLoc = _bytecode.EmitNumberOperandPlaceholder();
			if (ternary.WhenFalse is TernaryExpr nested) EmitTernary(nested, successLoc);
			else
			{
				Emit(ternary.WhenFalse);
				_bytecode.PatchShort(successLoc, _bytecode.OpIndex);
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
				if (!IsBooleanExpr(terms[i])) _bytecode.Emit(Op.ConvToFloat);
				_bytecode.Emit(logical.Op == "&&" ? Op.And : Op.Or);
				patches.Add(_bytecode.EmitNumberOperandPlaceholder());
			}
			Emit(terms[^1], false, false, 0);
			if (!IsBooleanExpr(terms[^1])) _bytecode.Emit(Op.ConvToFloat);
			if (logicalInline) _bytecode.Emit(Op.InlineConditional);
			var target = _bytecode.OpIndex - (logicalInline ? 1 : 0) + (!logicalInline ? suppressedLogicalPatchOffset : 0);
			foreach (var patch in patches) _bytecode.PatchShort(patch, target);
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
			_bytecode.Emit(Op.ConvToObject);
			foreach (var index in expr.Indices)
			{
				Emit(index);
				if (!IsNumberExpr(index)) _bytecode.Emit(Op.ConvToFloat);
			}
			if (!assignmentTarget) _bytecode.Emit(Op.ArrayMultiDim);
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
			["max"] = Op.Max
		};

		private static readonly Dictionary<string, Op> NonReversedBuiltInCalls = new(StringComparer.Ordinal)
		{
			["pow"] = Op.Pow,
			["getangle"] = Op.GetAngle,
			["getdir"] = Op.GetDir
		};

		private static readonly HashSet<string> NonReturningBuiltInCalls = new(StringComparer.Ordinal)
		{
			"sleep",
			"setarray"
		};

		private static readonly HashSet<string> NonReturningMethodCalls = new(StringComparer.Ordinal)
		{
			"clear",
			"add",
			"delete",
			"insert",
			"remove",
			"replace"
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
			_ => true
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
			_ => ExprType.Unknown
		};

		private static bool IsBooleanExpr(Expr expr) => expr switch
		{
			BinaryExpr { Op: "==" or "!=" or "<" or "<=" or "=<" or ">" or ">=" or "=>" or "&&" or "||" } => true,
			InExpr => true,
			UnaryExpr { Op: "!" } => true,
			_ => false
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
			_ => false
		};

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
			_ => false
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
			_ => Op.None
		};

		private static Op CompoundOpcode(string op) => op switch
		{
			"<<=" => Op.ShiftLeft,
			">>=" => Op.ShiftRight,
			"^=" => Op.Pow,
			_ => BinaryOpcode(op[0].ToString())
		};
	}

	private sealed class BytecodeWriter
	{
		private readonly List<byte> _code = [];
		private readonly List<string> _strings = [];
		private readonly Dictionary<string, int> _stringIndex = new(StringComparer.Ordinal);
		private readonly List<FunctionEntry> _functions = [];
		private readonly HashSet<string> _functionSet = new(StringComparer.Ordinal);
		private readonly List<int> _prejumpPatches = [];
		private Op _lastOp = Op.None;
		public int OpIndex { get; private set; }

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
			WriteSegment(result, 1, [0, 0, 0, 0]);
			List<byte> functions = [];
			foreach (var function in OrderedFunctions())
			{
				WriteInt(functions, function.OpIndex);
				WriteCString(functions, function.Name);
			}
			WriteSegment(result, 2, functions);
			List<byte> strings = [];
			foreach (var str in _strings) WriteCString(strings, str);
			WriteSegment(result, 3, strings);
			WriteSegment(result, 4, _code);
			result.Add(10);
			return [.. result];
		}

		private IEnumerable<FunctionEntry> OrderedFunctions()
		{
			HashSet<string> emitted = new(StringComparer.Ordinal);
			foreach (var str in _strings)
				foreach (var fn in _functions)
					if (fn.Name == str && emitted.Add(fn.Name)) yield return fn;
			foreach (var fn in _functions)
				if (emitted.Add(fn.Name)) yield return fn;
		}

		private static void WriteSegment(List<byte> target, int type, IReadOnlyCollection<byte> bytes)
		{
			WriteInt(target, type);
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

	private sealed class Lexer
	{
		private readonly string _code;
		private int _pos;
		private int _line = 1;
		private int _lineStart;
		public Lexer(string code) => _code = code;

		public Token Next()
		{
			while (_pos < _code.Length)
			{
				var c = _code[_pos];
				if (c is ' ' or '\t' or '\r') { _pos++; continue; }
				if (c == '\n') { _pos++; _line++; _lineStart = _pos; continue; }
				if (c == '/' && Peek(1) == '/') { while (_pos < _code.Length && _code[_pos] != '\n') _pos++; continue; }
				if (c == '/' && Peek(1) == '*') { _pos += 2; while (_pos + 1 < _code.Length && !(_code[_pos] == '*' && _code[_pos + 1] == '/')) { if (_code[_pos++] == '\n') { _line++; _lineStart = _pos; } } if (_pos + 1 < _code.Length) _pos += 2; else _pos = _code.Length; continue; }
				break;
			}
			if (_pos >= _code.Length) return Make(TokenType.End, "");
			var ch = _code[_pos];
			if (ch == '0' && (Peek(1) == 'x' || Peek(1) == 'X')) return HexNumber();
			if (ch == '.' && char.IsDigit(Peek(1))) return Number();
			if (char.IsDigit(ch)) return Number();
			if (IsIdentStart(ch)) return Identifier();
			if (ch is '"' or '\'') return String();
			if (ch == ':' && Peek(1) == ':') { _pos += 2; return Make(TokenType.Scope, "::"); }
			if (ch == ':' && Peek(1) == '=') { _pos += 2; return Make(TokenType.Assign, ":="); }
			if (ch == '+' && Peek(1) == '=') { _pos += 2; return Make(TokenType.AddAssign, "+="); }
			if (ch == '-' && Peek(1) == '=') { _pos += 2; return Make(TokenType.SubAssign, "-="); }
			if (ch == '*' && Peek(1) == '=') { _pos += 2; return Make(TokenType.MulAssign, "*="); }
			if (ch == '/' && Peek(1) == '=') { _pos += 2; return Make(TokenType.DivAssign, "/="); }
			if (ch == '^' && Peek(1) == '=') { _pos += 2; return Make(TokenType.PowAssign, "^="); }
			if (ch == '%' && Peek(1) == '=') { _pos += 2; return Make(TokenType.ModAssign, "%="); }
			if (ch == '@' && Peek(1) == '=') { _pos += 2; return Make(TokenType.CatAssign, "@="); }
			if (ch == '<' && Peek(1) == '<' && Peek(2) == '=') { _pos += 3; return Make(TokenType.ShiftLeftAssign, "<<="); }
			if (ch == '>' && Peek(1) == '>' && Peek(2) == '=') { _pos += 3; return Make(TokenType.ShiftRightAssign, ">>="); }
			if (ch == '=' && Peek(1) == '=') { _pos += 2; return Make(TokenType.Equal, "=="); }
			if (ch == '!' && Peek(1) == '=') { _pos += 2; return Make(TokenType.NotEqual, "!="); }
			if (ch == '<' && Peek(1) == '>') { _pos += 2; return Make(TokenType.NotEqual, "!="); }
			if (ch == '<' && Peek(1) == '=') { _pos += 2; return Make(TokenType.LessEqual, "<="); }
			if (ch == '=' && Peek(1) == '<') { _pos += 2; return Make(TokenType.LessEqual, "=<"); }
			if (ch == '>' && Peek(1) == '=') { _pos += 2; return Make(TokenType.GreaterEqual, ">="); }
			if (ch == '=' && Peek(1) == '>') { _pos += 2; return Make(TokenType.GreaterEqual, "=>"); }
			if (ch == '&' && Peek(1) == '&') { _pos += 2; return Make(TokenType.And, "&&"); }
			if (ch == '|' && Peek(1) == '|') { _pos += 2; return Make(TokenType.Or, "||"); }
			if (ch == '<' && Peek(1) == '<') { _pos += 2; return Make(TokenType.ShiftLeft, "<<"); }
			if (ch == '>' && Peek(1) == '>') { _pos += 2; return Make(TokenType.ShiftRight, ">>"); }
			if (ch == '+' && Peek(1) == '+') { _pos += 2; return Make(TokenType.Increment, "++"); }
			if (ch == '-' && Peek(1) == '-') { _pos += 2; return Make(TokenType.Decrement, "--"); }
			_pos++;
			return ch switch
			{
				'=' => Make(TokenType.Assign, "="),
				';' => Make(TokenType.Semicolon, ";"),
				',' => Make(TokenType.Comma, ","),
				':' => Make(TokenType.Colon, ":"),
				'?' => Make(TokenType.Question, "?"),
				'.' => Make(TokenType.Dot, "."),
				'{' => Make(TokenType.LeftBrace, "{"),
				'}' => Make(TokenType.RightBrace, "}"),
				'(' => Make(TokenType.LeftParen, "("),
				')' => Make(TokenType.RightParen, ")"),
				'[' => Make(TokenType.LeftBracket, "["),
				']' => Make(TokenType.RightBracket, "]"),
				'-' => Make(TokenType.Minus, "-"),
				'+' => Make(TokenType.Plus, "+"),
				'*' => Make(TokenType.Star, "*"),
				'/' => Make(TokenType.Slash, "/"),
				'%' => Make(TokenType.Percent, "%"),
				'^' => Make(TokenType.Caret, "^"),
				'@' => Make(TokenType.At, "@"),
				'!' => Make(TokenType.Not, "!"),
				'~' => Make(TokenType.BitInvert, "~"),
				'<' => Make(TokenType.Less, "<"),
				'>' => Make(TokenType.Greater, ">"),
				'&' => Make(TokenType.BitAnd, "&"),
				'|' => Make(TokenType.BitOr, "|"),
				_ => Make(TokenType.Unknown, ch.ToString())
			};
		}

		private Token Number()
		{
			var start = _pos;
			while (_pos < _code.Length && char.IsDigit(_code[_pos])) _pos++;
			if (_pos < _code.Length && _code[_pos] == '.') { _pos++; while (_pos < _code.Length && char.IsDigit(_code[_pos])) _pos++; }
			return Make(TokenType.Number, _code[start.._pos]);
		}

		private Token HexNumber()
		{
			var start = _pos;
			_pos += 2;
			while (_pos < _code.Length && Uri.IsHexDigit(_code[_pos])) _pos++;
			var value = Convert.ToInt32(_code[start.._pos], 16);
			return Make(TokenType.Number, value.ToString(CultureInfo.InvariantCulture));
		}

		private Token Identifier()
		{
			var start = _pos++;
			while (_pos < _code.Length && IsIdentPart(_code[_pos])) _pos++;
			var text = _code[start.._pos];
			return Make(text switch
			{
				"const" => TokenType.Const,
				"enum" => TokenType.Enum,
				"function" => TokenType.Function,
				"public" => TokenType.Public,
				"return" => TokenType.Return,
				"if" => TokenType.If,
				"else" => TokenType.Else,
				"elseif" => TokenType.ElseIf,
				"for" => TokenType.For,
				"while" => TokenType.While,
				"with" => TokenType.With,
				"new" => TokenType.New,
				"in" => TokenType.In,
				"SPC" => TokenType.At,
				"NL" => TokenType.At,
				"TAB" => TokenType.At,
				"switch" => TokenType.Switch,
				"case" => TokenType.Case,
				"default" => TokenType.Default,
				"break" => TokenType.Break,
				"continue" => TokenType.Continue,
				"int" => TokenType.IntCast,
				"float" => TokenType.FloatCast,
				"_" => TokenType.Translate,
				"xor" => TokenType.BitXor,
				"true" => TokenType.True,
				"false" => TokenType.False,
				"null" => TokenType.Null,
				_ => TokenType.Identifier
			}, text switch { "SPC" => " ", "NL" => "\n", "TAB" => "\t", _ => text });
		}

		private Token String()
		{
			var quote = _code[_pos];
			_pos++;
			StringBuilder builder = new();
			while (_pos < _code.Length && _code[_pos] != quote)
			{
				if (_code[_pos] == '\\' && _pos + 1 < _code.Length)
				{
					_pos++;
					builder.Append(_code[_pos] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '"' => '"', '\'' => '\'', '\\' => '\\', var c => c });
					_pos++;
				}
				else if (_code[_pos] == '\r' && Peek(1) == '\n')
				{
					builder.Append('\n');
					_pos += 2;
					_line++;
					_lineStart = _pos;
				}
				else if (_code[_pos] == '\n')
				{
					builder.Append('\n');
					_pos++;
					_line++;
					_lineStart = _pos;
				}
				else builder.Append(_code[_pos++]);
			}
			if (_pos < _code.Length) _pos++;
			return Make(TokenType.String, builder.ToString());
		}

		private char Peek(int offset) => _pos + offset < _code.Length ? _code[_pos + offset] : '\0';
		private Token Make(TokenType type, string text) => new(type, text, _line, _pos - _lineStart + 1) { LineText = CurrentLine() };
		private string CurrentLine()
		{
			var end = _code.IndexOf('\n', _lineStart);
			if (end < 0) end = _code.Length;
			return _code[_lineStart..end];
		}
		private static bool IsIdentStart(char c) => char.IsLetter(c) || c is '_' or '$';
		private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
	}

	private enum TokenType { Unknown, End, Identifier, Number, String, Const, Enum, Function, Public, Return, If, Else, ElseIf, For, While, With, New, In, Switch, Case, Default, Break, Continue, IntCast, FloatCast, Translate, True, False, Null, Assign, AddAssign, SubAssign, MulAssign, DivAssign, PowAssign, ModAssign, CatAssign, ShiftLeftAssign, ShiftRightAssign, Semicolon, Comma, Colon, Question, Dot, Scope, LeftBrace, RightBrace, LeftParen, RightParen, LeftBracket, RightBracket, Minus, Plus, Star, Slash, Percent, Caret, At, Not, BitInvert, Equal, NotEqual, Less, LessEqual, Greater, GreaterEqual, And, Or, BitAnd, BitOr, BitXor, ShiftLeft, ShiftRight, Increment, Decrement }
	private sealed record Token(TokenType Type, string Text, int Line, int Column) { public string LineText { get; init; } = ""; }
	private sealed record ProgramNode(Dictionary<string, Expr> Constants, Dictionary<string, Dictionary<string, int>> Enums, List<ProgramItem> Items);
	private abstract record ProgramItem;
	private sealed record FunctionItem(FunctionNode Function) : ProgramItem;
	private sealed record StatementItem(Stmt Statement) : ProgramItem;
	private sealed record FunctionNode(string Name, string? ObjectName, bool Public, List<Expr> Args, List<Stmt> Body);
	private abstract record Stmt;
	private sealed record ExprStmt(Expr Expression) : Stmt;
	private sealed record InlineStmt(Stmt Statement) : Stmt;
	private sealed record BlockStmt(List<Stmt> Body) : Stmt;
	private sealed record ReturnStmt(Expr Expression) : Stmt;
	private sealed record IfStmt(Expr Condition, List<Stmt> ThenBody, List<Stmt> ElseBody, bool HasElse) : Stmt;
	private sealed record ForStmt(Expr? Init, Expr Condition, Expr? Post, List<Stmt> Body) : Stmt;
	private sealed record ForEachStmt(Expr Name, Expr Source, List<Stmt> Body) : Stmt;
	private sealed record WhileStmt(Expr Condition, List<Stmt> Body) : Stmt;
	private sealed record WithStmt(Expr Target, List<Stmt> Body) : Stmt;
	private sealed record SwitchStmt(Expr Expression, List<SwitchCase> Cases) : Stmt;
	private sealed record NewStmt(string TypeName, List<Expr> Args, List<Stmt> Body) : Stmt;
	private sealed record BreakStmt : Stmt;
	private sealed record ContinueStmt : Stmt;
	private sealed record SwitchCase(List<Expr?> Labels, List<Stmt> Body);
	private abstract record Expr;
	private sealed record BinaryExpr(Expr Left, string Op, Expr Right) : Expr;
	private sealed record InExpr(Expr Expression, Expr Lower, Expr? Upper) : Expr;
	private sealed record TernaryExpr(Expr Condition, Expr WhenTrue, Expr WhenFalse) : Expr;
	private sealed record UnaryExpr(string Op, Expr Expression, bool Postfix = false) : Expr;
	private sealed record StringCastExpr(Expr Expression) : Expr;
	private sealed record CastExpr(string Type, Expr Expression) : Expr;
	private sealed record MemberExpr(Expr Object, string Name) : Expr;
	private sealed record DynamicMemberExpr(Expr Object, Expr Name) : Expr;
	private sealed record DynamicVarExpr(Expr Name) : Expr;
	private sealed record ArrayIndexExpr(Expr Target, Expr Index) : Expr;
	private sealed record MultiArrayIndexExpr(Expr Target, List<Expr> Indices) : Expr;
	private sealed record IdentifierExpr(string Name) : Expr;
	private sealed record EnumExpr(string EnumName, string MemberName) : Expr;
	private sealed record NumberExpr(string Text) : Expr;
	private sealed record StringExpr(string Value) : Expr;
	private sealed record BoolExpr(bool Value) : Expr;
	private sealed record NullExpr : Expr;
	private sealed record CallExpr(string Name, List<Expr> Args) : Expr;
	private sealed record ChainedCallExpr(CallExpr Call, List<Expr> Args) : Expr;
	private sealed record MethodCallExpr(Expr Object, string Name, List<Expr> Args) : Expr;
	private sealed record DynamicMethodCallExpr(Expr Object, Expr Name, List<Expr> Args) : Expr;
	private sealed record NewObjectExpr(string TypeName, List<Expr> Args) : Expr;
	private sealed record NewArrayExpr(List<Expr> Dimensions) : Expr;
	private sealed record LambdaExpr(string Name, List<Expr> Args, List<Stmt> Body) : Expr;
	private sealed record ArrayLiteralExpr(List<Expr> Values) : Expr;
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
		Temp = 189
	}
}
