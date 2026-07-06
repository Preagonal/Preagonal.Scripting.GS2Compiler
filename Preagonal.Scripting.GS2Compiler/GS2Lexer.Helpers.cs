using System.Text;

public partial class GS2Lexer
{
	private static string UnescapeString(string text)
	{
		StringBuilder builder = new();
		for (var i = 0; i < text.Length; ++i)
		{
			if (text[i] == '\\' && i + 1 < text.Length)
			{
				i++;
				builder.Append(text[i] switch
				{
					'n' => '\n',
					't' => '\t',
					'r' => '\r',
					'"' => '"',
					'\'' => '\'',
					'\\' => '\\',
					var c => c
				});
			}
			else builder.Append(text[i]);
		}
		return builder.ToString();
	}
}
