using Antlr4.Runtime;

public partial class GS1Lexer
{
	private enum POPMODE
	{
		POPMODE_COMMAND,
		POPMODE_FUNCTION,
		POPMODE_ARRAYINDEX
	}

	private const POPMODE POPMODE_FUNCTION = POPMODE.POPMODE_FUNCTION;

	private sealed class CommandState
	{
		public string Arguments { get; private set; }
		public int Index { get; private set; }
		public POPMODE popMode;
		public bool commaPop;

		public CommandState(string arguments, POPMODE popMode, bool commaPop)
		{
			Arguments = arguments;
			this.popMode = popMode;
			this.commaPop = commaPop;
		}

		public bool HasArguments => Index < Arguments.Length;
		public char CurrentArgument => Arguments[Index];
		public char PopArgument() => Arguments[Index++];
		public void Terminate() => Index = Arguments.Length;
	}

	private static readonly string[] builtInCommands =
	[
		"gr-debugger",
		"setstring",
		"addstring",
		"insertstring",
		"replacestring",
		"removestring",
		"deletestring",
		"set ",
		"unset ",
		"sleep",
		"setarray",
		"timereverywhere",
		"setgif ",
		"setimg",
		"setimgpart",
		"hide",
		"show",
		"dontblock",
		"blockagain",
		"drawoverplayer",
		"drawovertrees",
		"drawunderplayer",
		"drawaslight",
		"seteffectmode ",
		"canbecarried",
		"cannotbecarried",
		"canbepushed",
		"cannotbepushed",
		"canbepulled",
		"cannotbepulled",
		"move ",
		"say ",
		"say2",
		"lay ",
		"lay2",
		"take ",
		"take2",
		"message",
		"timershow",
		"showcharacter",
		"setcharprop",
		"setcharani",
		"setchargender",
		"triggeraction",
		"putnpc",
		"putnpc2",
		"callnpc",
		"callweapon",
		"destroy",
		"carryobject",
		"throwcarry",
		"followplayer",
		"toinventory",
		"toweapons",
		"setcoloreffect",
		"setzoomeffect",
		"showimg",
		"showimg2",
		"showani",
		"showani2",
		"showpoly",
		"showpoly2",
		"showtext",
		"showtext2",
		"hideimg",
		"hideimgs",
		"changeimgpart",
		"changeimgvis",
		"changeimgcolors",
		"changeimgzoom",
		"changeimgmode",
		"shootarrow",
		"shootfireball",
		"shootfireblast",
		"shootnuke",
		"shootball",
		"spyfire",
		"hitplayer",
		"hitnpc",
		"hitobjects",
		"hidelocal",
		"showlocal",
		"dontblocklocal",
		"blockagainlocal",
		"takehorse",
		"tokenize ",
		"tokenize2",
		"setshape ",
		"setshape2",
		"wraptext ",
		"wraptext2 ",
		"setshootparams ",
		"shoot ",
		"setlevel ",
		"setlevel2",
		"seturllevel",
		"setbody",
		"sethead",
		"setsword",
		"setshield",
		"setbow",
		"setani",
		"setplayerdir",
		"setgender",
		"setskincolor",
		"setcoatcolor",
		"setsleevecolor",
		"setshoecolor",
		"setbeltcolor",
		"setplayerprop",
		"takeplayercarry",
		"takeplayerhorse",
		"disableweapons",
		"enableweapons",
		"freezeplayer ",
		"freezeplayer2",
		"unfreezeplayer",
		"hideplayer",
		"hidesword",
		"hurt ",
		"disabledefmovement",
		"enabledefmovement",
		"disableselectweapons",
		"enableselectweapons",
		"disablepause",
		"enablepause",
		"disablemap",
		"enablemap",
		"enablefeatures",
		"replaceani",
		"attachplayertoobj",
		"detachplayer",
		"updateboard",
		"updateboard2",
		"putobject",
		"putbomb",
		"putexplosion ",
		"putexplosion2",
		"putleaps",
		"puthorse",
		"setbackpal",
		"setbacktile",
		"setbacktile2",
		"setletters",
		"setmap",
		"setminimap",
		"seteffect ",
		"setfocus",
		"resetfocus",
		"noplayerkilling",
		"noplayeronwall",
		"removebomb",
		"removearrow",
		"removeitem",
		"removeexplo",
		"removehorse",
		"explodebomb",
		"reflectarrow",
		"addtiledef ",
		"addtiledef2",
		"removetiledefs",
		"loadmap",
		"updateterrain",
		"showstats",
		"putcomp",
		"putnewcomp",
		"hitcompu",
		"removecompus",
		"play ",
		"play2 ",
		"playlooped",
		"stopsound",
		"stopmidi",
		"setmusicvolume",
		"openurl ",
		"openurl2 ",
		"showfile",
		"join",
		"setcursor ",
		"setcursor2",
		"canwarp",
		"canwarp2",
		"cannotwarp",
		"addweapon",
		"removeweapon",
		"setspritesimage",
		"setstatusimage",
		"addguildmember",
		"removeguildmember",
		"removeguild",
		"copystrings",
		"sendtorc",
		"sendtonc",
		"sendpm",
		"setpm",
		"sendrpgmessage",
		"serverwarp",
		"setz ",
		"copylevel",
		"deletelevel",
		"saveinfo",
		"savelog",
		"savelog2",
		"warpto",
		"enabledamagereactions",
		"disabledamagereactions"
	];

	private int m_braceCount;
	private readonly System.Collections.Generic.List<CommandState> m_commandStates = [];
	private readonly System.Collections.Generic.LinkedList<IToken> m_pendingTokensBefore = [];
	private readonly System.Collections.Generic.Queue<IToken> m_pendingTokensAfter = [];

	public override IToken NextToken()
	{
		if (m_pendingTokensBefore.Count > 0)
		{
			var token = m_pendingTokensBefore.First!.Value;
			m_pendingTokensBefore.RemoveFirst();
			return token;
		}
		if (m_pendingTokensAfter.Count > 0) return m_pendingTokensAfter.Dequeue();

		var next = base.NextToken();
		if (m_pendingTokensBefore.Count > 0)
		{
			var token = m_pendingTokensBefore.First!.Value;
			m_pendingTokensBefore.RemoveFirst();
			m_pendingTokensBefore.AddFirst(next);
			return token;
		}
		return next;
	}

	private static bool isBuiltInCommand(string name)
	{
		foreach (var builtIn in builtInCommands)
			if (name.StartsWith(builtIn, System.StringComparison.Ordinal))
				return true;
		return false;
	}

	private void emitIdentifierBefore(int type, string name)
	{
		_ = name;
		m_pendingTokensBefore.AddLast(TokenFactory.Create(type, ""));
	}

	private void emitIdentifierAfter(int type, string name)
	{
		_ = name;
		m_pendingTokensAfter.Enqueue(TokenFactory.Create(type, ""));
	}

	private int breakpoint() => InputStream.Index;

	private bool canFuncPop() => m_commandStates.Count > 0 && CurrentState.popMode == POPMODE.POPMODE_FUNCTION;
	private bool canCmdPop() => m_commandStates.Count == 0 || CurrentState.popMode == POPMODE.POPMODE_COMMAND;
	private bool canArrayPop() => m_commandStates.Count > 0 && CurrentState.popMode == POPMODE.POPMODE_ARRAYINDEX;
	private bool canCommaPop() => m_commandStates.Count == 0 || CurrentState.commaPop;
	private bool isNextArgLeftParen() => m_commandStates.Count > 0 && CurrentState.HasArguments && CurrentState.CurrentArgument == '(';
	private bool isNotDefaultMode() => m_commandStates.Count > 0;

	private CommandState CurrentState => m_commandStates[m_commandStates.Count - 1];

	private void setCurrentPopMode(POPMODE popMode)
	{
		if (m_commandStates.Count > 0) CurrentState.popMode = popMode;
	}

	private void pushCommand(string arguments)
	{
		m_commandStates.Add(new(arguments, POPMODE.POPMODE_COMMAND, true));
		PushMode(IN_PARAM_1);
		popNextMode();
	}

	private void pushArrayAccess()
	{
		m_commandStates.Add(new("P", POPMODE.POPMODE_ARRAYINDEX, false));
		PushMode(IN_PARAM_1);
		popNextMode();
	}

	private void checkIfNextModeOptional()
	{
		popNextMode();

		var skipNext = false;
		try
		{
			var index = 1;
			int symbol;
			while ((symbol = InputStream.LA(index++)) != TokenConstants.EOF)
			{
				if (symbol == ')')
				{
					skipNext = true;
					break;
				}
				if (symbol == ',') break;
			}
		}
		catch
		{
		}

		if (skipNext) popNextMode();
	}

	private void popNextMode(bool terminateEarly = false)
	{
		if (m_commandStates.Count == 0) return;
		var currentState = CurrentState;

		if (terminateEarly) currentState.Terminate();
		if (!currentState.HasArguments)
		{
			PopMode();
			m_commandStates.RemoveAt(m_commandStates.Count - 1);
			return;
		}

		var mode = currentState.PopArgument();
		if ((mode == 'S' || mode == 'R') && (!currentState.HasArguments || currentState.CurrentArgument == ')'))
			currentState.commaPop = false;

		switch (mode)
		{
			case 'V': Mode(IN_PARAM_V); emitIdentifierAfter(GS1Lexer.IDENTIFIER, Text); break;
			case 'E': Mode(IN_PARAM_E); break;
			case 'P': Mode(IN_PARAM_E); currentState.commaPop = false; break;
			case 'S': Mode(IN_PARAM_S); emitIdentifierAfter(GS1Lexer.STRING, Text); break;
			case 'R': Mode(IN_PARAM_R); emitIdentifierAfter(GS1Lexer.STRING, Text); break;
			case 'L': Mode(IN_PARAM_L); emitIdentifierAfter(GS1Lexer.STRING, Text); break;
			case 'M': Mode(IN_PARAM_M); emitIdentifierAfter(GS1Lexer.RAWMESSAGECODE, Text); break;
			case 'B': Mode(IN_PARAM_B); break;
			case 'I': Mode(IN_PARAM_I); break;
			case 'C': Mode(IN_PARAM_C); break;
			case 'G': Mode(IN_PARAM_G); break;
			case 'U': Mode(IN_PARAM_U); break;
			case 'D': Mode(IN_PARAM_D); break;
			case 'X': Mode(IN_PARAM_X); break;
			case 'Z': Mode(IN_PARAM_Z); break;
			case '(': Mode(IN_PARAM_1); break;
			case ')': Mode(IN_PARAM_2); break;
			case '<': Mode(IN_PARAM_3); break;
			default: Mode(DEFAULT_MODE); break;
		}
	}
}
