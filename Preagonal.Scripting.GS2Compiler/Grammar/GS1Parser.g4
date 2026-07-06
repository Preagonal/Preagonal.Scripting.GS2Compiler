parser grammar GS1Parser;

options
{
	tokenVocab=GS1Lexer;
}

program
	: EOF
	| block+
	;

block
	: TOKEN_BRACE_LEFT statement* TOKEN_BRACE_RIGHT
	| statement
	;

statement
	: END
	| ( ifStatement
		| forStatement
		| whileStatement
		| withStatement
		| functionDefinition
		)
	| ( flowStatement
		| builtinCommandStatement
		| userFunctionStatement
		| assignmentStatement
		| expression
		) (END | EOF)
	;

//----------------------------------------------------------

ifStatement
	: KW_IF TOKEN_PAREN_LEFT expression TOKEN_PAREN_RIGHT block (KW_ELSE block)?			# StatementIf
	;

forStatement
    : KW_FOR TOKEN_PAREN_LEFT
		assignmentStatement? END
		expression? END
		(assignmentStatement | expression)? TOKEN_PAREN_RIGHT
		block																				# StatementFor
    ;

whileStatement
    : KW_WHILE TOKEN_PAREN_LEFT expression TOKEN_PAREN_RIGHT block							# StatementWhile
    ;

withStatement
	: KW_WITH TOKEN_PAREN_LEFT expression TOKEN_PAREN_RIGHT block							# StatementWith
	;

flowStatement
    : KW_RETURN																				# FlowReturn
    | KW_BREAK																				# FlowBreak
    | KW_CONTINUE																			# FlowContinue
    ;

//----------------------------------------------------------

functionDefinition
	: KW_FUNCTION compound_identifier TOKEN_PAREN_LEFT TOKEN_PAREN_RIGHT block				# StatementFunctionDefinition
	;

//----------------------------------------------------------

userFunctionStatement
	: compound_identifier TOKEN_PAREN_LEFT TOKEN_PAREN_RIGHT								# StatementUserFunctionCall
	;

//----------------------------------------------------------

builtinCommandStatement
	: COMMAND builtInCommandExpression (TOKEN_COMMA builtInCommandExpression)*				# StatementBuiltInCommand
	| COMMAND																				# StatementBuiltInCommand
	;

builtInCommandExpression
	: special_literal
	| expression
	;

//----------------------------------------------------------

assignmentStatement
	: identifier_access assignment_operator expression										# StatementAssignment
	;

//----------------------------------------------------------

expression
	: logicalOrExpression (TOKEN_QUESTION expression TOKEN_COLON expression)*				# ExpressionTernary
	;

logicalOrExpression
	: logicalAndExpression (OP_LOGICALOR logicalAndExpression)*								# ExpressionLogicOr
	;

logicalAndExpression
	: equalityExpression (OP_LOGICALAND equalityExpression)*								# ExpressionLogicAnd
	;

equalityExpression
	: relationalExpression ((OP_EQUAL | OP_ASSIGN | OP_NOTEQ) relationalExpression)?		# ExpressionEquality
	;

relationalExpression
	: additiveExpression
		((OP_LESS | OP_GREAT | OP_LESS_EQ | OP_GREAT_EQ) additiveExpression)?				# ExpressionRelational
	;

additiveExpression
	: multiplicativeExpression ((OP_ADD | OP_SUB) (LITERAL | multiplicativeExpression))*	# ExpressionAdditive
	;

multiplicativeExpression
	: inExpression ((OP_MUL | OP_DIV | OP_MOD) (LITERAL | inExpression))*					# ExpressionMultiplicative
	;

inExpression
	: exponentiationExpression
		((TOKEN_COMMA exponentiationExpression)*
			OP_IN (range_literal | primaryExpression))?										# ExpressionIn
	;

exponentiationExpression
	: unaryExpression (OP_POW unaryExpression)*												# ExpressionExponentiation
	;

unaryExpression
	: (OP_ADD | OP_SUB | OP_LOGICALNOT) unaryExpression										# ExpressionUnary
	| postfixExpression																		# ignoreExpressionUnaryPrimary
	;

postfixExpression
	: primaryExpression (OP_INC | OP_DEC)													# ExpressionPostfix
	| primaryExpression																		# ignoreExpressionPostfixPrimary
	;

primaryExpression
	: TOKEN_PAREN_LEFT expression TOKEN_PAREN_RIGHT
	| RAWMESSAGECODE messagecode_string
	| builtin_function
	| array_literal
	| literal_literal
	| identifier_access
	| compound_string
	;

//----------------------------------------------------------

builtin_function
	: FUNCTION TOKEN_PAREN_LEFT expression (TOKEN_COMMA expression)* TOKEN_PAREN_RIGHT																					# BuiltInFunctionCall
	;

identifier_access
	: identifier_value (TOKEN_PERIOD identifier_value)*										# IdentifierAccess
	;

identifier_value
	: compound_identifier
		(TOKEN_BRACKET_LEFT
			expression (TOKEN_COMMA expression)?
		TOKEN_BRACKET_RIGHT)?							# IdentifierValue
	;

compound_identifier
	: (IDENTIFIER | messagecode_string | REAL | TOKEN_PERIOD)+								# CompoundIdentifier
	;

compound_string
	: (STRING | messagecode_string)+														# CompoundString
	;

messagecode_string
	: MESSAGECODE
		(TOKEN_PAREN_LEFT expression (TOKEN_COMMA expression)* TOKEN_PAREN_RIGHT)?			# MessageCode
	;

//----------------------------------------------------------

assignment_operator
	: ( OP_ASSIGN
		| OP_ASSIGN_MUL
		| OP_ASSIGN_DIV
		| OP_ASSIGN_MOD
		| OP_ASSIGN_ADD
		| OP_ASSIGN_SUB
		| OP_ASSIGN_POW
		)
	;

array_literal
	: TOKEN_BRACE_LEFT (TOKEN_COMMA | expression)* END? TOKEN_BRACE_RIGHT					# ArrayLiteral
	;

literal_literal
	: ( LITERAL
		| ALLFEATURES
		| ALLSTATS )																		# Literal
	;

range_literal
	: (TOKEN_PIPE | OP_LESS) expression TOKEN_COMMA expression (TOKEN_PIPE | OP_GREAT)		# RangeLiteral
	;

special_literal
	: ITEM																					# ItemLiteral
	| CARRY																					# CarryLiteral
	| DIRECTION																				# DirectionLiteral
	| GENDER																				# GenderLiteral
	| COLOR																					# ColorLiteral
	| BADDY																					# BaddyLiteral
	;
