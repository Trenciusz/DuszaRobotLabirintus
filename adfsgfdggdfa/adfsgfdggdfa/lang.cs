using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

// A programblokkok (utasítások) lehetséges típusai.
static class CommandType
{
    public const string Step = "step";
    public const string Turn = "turn";
    public const string Mine = "mine";
    public const string Print = "print";
    public const string Set = "set";
    public const string If = "if";
    public const string While = "while";
}

// Egy programblokk (utasítás).
// Az "if" ElseBody listája null, ha nincs else ág.
class Command
{
    public string Type;                                    // lásd: CommandType
    public string[] Arguments = new string[0];             // a blokk mezői (set: [0] = változónév, [1..] = kifejezés tokenek)
    public List<Command> Body = new List<Command>();       // if / while törzse
    public List<Command> ElseBody;                         // csak if esetén, ha van else ág
    public int LineNumber;                                 // a sor száma a szöveges kódban
    public int ElseLineNumber;                             // az "else" sor száma a szöveges kódban

    // Igaz, ha a blokknak van törzse (if vagy while).
    public bool IsControlBlock
    {
        get { return Type == CommandType.If || Type == CommandType.While; }
    }

    // A blokk szöveges forráskódként (egy sor, behúzás nélkül).
    public string ToSourceText()
    {
        switch (Type)
        {
            case CommandType.Step:
                return "step forward " + Arguments[0];

            case CommandType.Turn:
                return "turn by " + Arguments[0];

            case CommandType.Mine:
                return "mine here";

            case CommandType.Print:
                return "print " + Arguments[0];

            case CommandType.Set:
                {
                    string expression = string.Join(" ", Arguments.Skip(1));
                    return "set " + Arguments[0] + " to " + expression;
                }

            case CommandType.If:
                return "if " + string.Join(" ", Arguments);

            case CommandType.While:
            default:
                return "while " + string.Join(" ", Arguments);
        }
    }

    // A blokk a blokk-nézetben: a szerkeszthető mezők [szögletes zárójelben] vannak.
    public string ToBlockViewText()
    {
        switch (Type)
        {
            case CommandType.Step:
                return "step forward [" + Arguments[0] + "]";

            case CommandType.Turn:
                return "turn by [" + Arguments[0] + "]";

            case CommandType.Mine:
                return "mine here";

            case CommandType.Print:
                return "print [" + Arguments[0] + "]";

            case CommandType.Set:
                return "set [" + Arguments[0] + "] to " + ExpressionToBlockViewText(Arguments.Skip(1));

            case CommandType.If:
                {
                    string text = "if " + ExpressionToBlockViewText(Arguments);
                    if (ElseBody != null)
                    {
                        text += "  (if-else)";
                    }
                    return text;
                }

            case CommandType.While:
            default:
                return "while " + ExpressionToBlockViewText(Arguments);
        }
    }

    // Egy kifejezés a blokk-nézetben: az operandusok [szögletes zárójelben] vannak,
    // a műveleti jelek (és az isThere / not) nem szerkeszthetők, ezért nem kapnak zárójelet.
    static string ExpressionToBlockViewText(IEnumerable<string> expressionTokens)
    {
        var expressionParts = new List<string>();
        foreach (string token in expressionTokens)
        {
            if (Language.Operators.Contains(token))
            {
                expressionParts.Add(token);
            }
            else
            {
                expressionParts.Add("[" + token + "]");
            }
        }
        return string.Join(" ", expressionParts);
    }
}

// Egy beolvasott forrássor: a behúzás mértéke és a szavakra (tokenekre) bontott tartalma.
class SourceLine
{
    public int Indent;
    public string[] Tokens;
}

// A nyelv szabályai: kulcsszavak, validáció, beolvasás, kiírás.
static class Language
{
    // A szerkesztőparancsokban használható "if-else blokk" kulcsszó (a programban ez "if" típusú, else ággal).
    const string IfElseKeyword = "ifelse";

    public static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "and", "by", "else", "false", "forward", "here", "if", "isThere", "mine",
        "not", "or", "print", "set", "step", "to", "true", "turn", "while"
    };

    // Kétoperandusú műveletek.
    public static readonly HashSet<string> BinaryOperators = new HashSet<string>
    {
        "+", "-", "*", "=", "<>", "<", "<=", ">", ">=", "and", "or"
    };

    // Minden művelet (a kétoperandusúak + az egyoperandusúak).
    public static readonly HashSet<string> Operators = new HashSet<string>(BinaryOperators) { "isThere", "not" };

    // Azonosító: betűvel kezdődik, betű/szám követi, és nem kulcsszó.
    public static bool IsIdentifier(string text)
    {
        bool hasValidShape = Regex.IsMatch(text, @"^[A-Za-z][A-Za-z0-9]*$");
        return hasValidShape && !Keywords.Contains(text);
    }

    // "Egyszerű érték": true / false, 0..100 közötti szám vagy azonosító.
    public static bool IsSimpleValue(string text)
    {
        if (text == "true" || text == "false")
        {
            return true;
        }

        bool looksLikeNumber = Regex.IsMatch(text, @"^[0-9]{1,3}$");
        if (looksLikeNumber && int.Parse(text) <= 100)
        {
            return true;
        }

        return IsIdentifier(text);
    }

    // Egy kifejezés (a "set" jobb oldala, illetve az if / ifelse / while feltétele) érvényes-e:
    //   <érték>  |  isThere <érték>  |  not <érték>  |  <érték> <művelet> <érték>
    static bool IsValidExpression(string[] expression)
    {
        if (expression.Length == 1)
        {
            return IsSimpleValue(expression[0]);
        }

        if (expression.Length == 2)
        {
            bool hasUnaryOperator = expression[0] == "isThere" || expression[0] == "not";
            return hasUnaryOperator && IsSimpleValue(expression[1]);
        }

        if (expression.Length == 3)
        {
            return IsSimpleValue(expression[0])
                && BinaryOperators.Contains(expression[1])
                && IsSimpleValue(expression[2]);
        }

        return false;
    }

    // Visszatérési érték: null, ha érvényes a blokk, különben a hibaüzenet.
    // ("ifelse X" = if-else blokk)
    public static string Validate(string[] tokens)
    {
        if (tokens.Length == 0)
        {
            return "Üres blokk.";
        }

        bool isValid;
        string usage;

        switch (tokens[0])
        {
            case CommandType.Step:
                usage = "step forward <érték>";
                isValid = tokens.Length == 3 && tokens[1] == "forward" && IsSimpleValue(tokens[2]);
                break;

            case CommandType.Turn:
                usage = "turn by <érték>";
                isValid = tokens.Length == 3 && tokens[1] == "by" && IsSimpleValue(tokens[2]);
                break;

            case CommandType.Mine:
                usage = "mine here";
                isValid = tokens.Length == 2 && tokens[1] == "here";
                break;

            case CommandType.Print:
                usage = "print <érték>";
                isValid = tokens.Length == 2 && IsSimpleValue(tokens[1]);
                break;

            case CommandType.If:
            case IfElseKeyword:
            case CommandType.While:
                usage = tokens[0] + " <érték | isThere x | not x | x művelet y>";
                isValid = IsValidExpression(tokens.Skip(1).ToArray());
                break;

            case CommandType.Set:
                usage = "set <változó> to <érték | isThere x | not x | x művelet y>";
                if (tokens.Length < 4 || tokens[2] != "to")
                {
                    isValid = false;
                    break;
                }
                if (!IsIdentifier(tokens[1]))
                {
                    return "Érvénytelen változónév (kulcsszó vagy hibás azonosító): " + tokens[1];
                }
                isValid = IsValidExpression(tokens.Skip(3).ToArray());
                break;

            default:
                return "Ismeretlen blokk: " + tokens[0];
        }

        if (isValid)
        {
            return null;
        }
        return "Érvénytelen blokk / mező. Forma: " + usage + " (azonosító: betű+betű/szám, nem kulcsszó; szám: 0..100)";
    }

    // Tokenekből blokkot (Command) készít. A tokeneknek előbb át kell menniük a Validate-en.
    public static Command CreateCommand(string[] tokens)
    {
        var command = new Command();

        switch (tokens[0])
        {
            case CommandType.Step:
            case CommandType.Turn:
                // "step forward X" / "turn by X": az érték a harmadik token
                command.Type = tokens[0];
                command.Arguments = new[] { tokens[2] };
                break;

            case CommandType.Mine:
                command.Type = CommandType.Mine;
                break;

            case CommandType.Print:
                command.Type = tokens[0];
                command.Arguments = new[] { tokens[1] };
                break;

            case CommandType.If:
            case CommandType.While:
                // "if / while <kifejezés>": a feltétel tokenjei (1 vagy több)
                command.Type = tokens[0];
                command.Arguments = tokens.Skip(1).ToArray();
                break;

            case IfElseKeyword:
                // Ez is "if" típusú, de üres else ággal.
                command.Type = CommandType.If;
                command.Arguments = tokens.Skip(1).ToArray();
                command.ElseBody = new List<Command>();
                break;

            case CommandType.Set:
                // "set V to <kifejezés>": [0] = V, utána a kifejezés tokenjei (a "to" kimarad)
                command.Type = CommandType.Set;
                command.Arguments = new[] { tokens[1] }.Concat(tokens.Skip(3)).ToArray();
                break;
        }

        return command;
    }

    // ---------- Beolvasás szöveges forráskódból ----------

    public static List<Command> ParseSource(IEnumerable<string> sourceLines)
    {
        var lines = new List<SourceLine>();

        foreach (string rawLine in sourceLines)
        {
            string line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                continue; // az üres sorokat kihagyjuk
            }

            int indent = line.Length - line.TrimStart(' ').Length;
            string[] tokens = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            lines.Add(new SourceLine { Indent = indent, Tokens = tokens });
        }

        int position = 0;
        return ParseBlock(lines, ref position, 0);
    }

    // Egy azonos behúzású blokk-sorozatot olvas be, a 'position' sortól kezdve.
    // Az "else" sornál megáll (azt az if feldolgozása kezeli).
    static List<Command> ParseBlock(List<SourceLine> lines, ref int position, int indent)
    {
        var commands = new List<Command>();

        while (position < lines.Count
               && lines[position].Indent == indent
               && lines[position].Tokens[0] != "else")
        {
            Command command = CreateCommand(lines[position].Tokens);
            position++;

            if (command.IsControlBlock)
            {
                // A törzs a következő, nagyobb behúzású sorokból áll.
                if (position < lines.Count && lines[position].Indent > indent)
                {
                    command.Body = ParseBlock(lines, ref position, lines[position].Indent);
                }

                // Az if után közvetlenül, azonos behúzással jöhet az "else".
                bool hasElseLine = command.Type == CommandType.If
                                   && position < lines.Count
                                   && lines[position].Indent == indent
                                   && lines[position].Tokens[0] == "else";
                if (hasElseLine)
                {
                    position++;
                    command.ElseBody = new List<Command>();
                    if (position < lines.Count && lines[position].Indent > indent)
                    {
                        command.ElseBody = ParseBlock(lines, ref position, lines[position].Indent);
                    }
                }
            }

            commands.Add(command);
        }

        return commands;
    }

    // ---------- Sorszámozás ----------

    // Sorszámot ad minden blokknak (és az else soroknak), a szöveges kód sorrendjében.
    // Visszatérési érték: az utolsó kiosztott sorszám.
    public static int AssignLineNumbers(List<Command> commands, int lastNumber = 0)
    {
        foreach (Command command in commands)
        {
            lastNumber++;
            command.LineNumber = lastNumber;
            lastNumber = AssignLineNumbers(command.Body, lastNumber);

            if (command.ElseBody != null)
            {
                lastNumber++;
                command.ElseLineNumber = lastNumber;
                lastNumber = AssignLineNumbers(command.ElseBody, lastNumber);
            }
        }
        return lastNumber;
    }

    // ---------- Szöveges forráskód előállítása (2 szóköz behúzással) ----------

    public static List<string> RenderSourceCode(List<Command> commands)
    {
        var output = new List<string>();
        RenderSourceCodeInto(commands, 0, output);
        return output;
    }

    static void RenderSourceCodeInto(List<Command> commands, int indent, List<string> output)
    {
        string indentText = new string(' ', indent);

        foreach (Command command in commands)
        {
            output.Add(indentText + command.ToSourceText());
            RenderSourceCodeInto(command.Body, indent + 2, output);

            if (command.ElseBody != null)
            {
                output.Add(indentText + "else");
                RenderSourceCodeInto(command.ElseBody, indent + 2, output);
            }
        }
    }

    // ---------- Blokk-nézet ----------

    // Útvonal-címkékkel ellátott blokk-nézet. Ezekkel a címkékkel lehet hivatkozni
    // a pozíciókra a szerkesztőparancsokban (pl. 3, 3.2, 3.e.1).
    public static void BuildBlockView(List<Command> commands, string pathPrefix, int indent, List<string> output)
    {
        string indentText = new string(' ', indent);

        for (int i = 0; i < commands.Count; i++)
        {
            Command command = commands[i];
            string path = pathPrefix + (i + 1);

            // A címke után mindig marad legalább egy szóköz (pl. "3.2.e.1 step forward [1]"); a rövidebb címkék 6 karakterre igazítva maradnak.
            output.Add(indentText + "┌ " + path.PadRight(Math.Max(6, path.Length + 1)) + command.ToBlockViewText());

            if (!command.IsControlBlock)
            {
                continue;
            }

            if (command.Body.Count == 0)
            {
                output.Add(indentText + "│   (üres blokk: " + path + ")");
            }
            BuildBlockView(command.Body, path + ".", indent + 4, output);

            if (command.ElseBody != null)
            {
                output.Add(indentText + "│  else:");
                if (command.ElseBody.Count == 0)
                {
                    output.Add(indentText + "│   (üres blokk: " + path + ".e)");
                }
                BuildBlockView(command.ElseBody, path + ".e.", indent + 4, output);
            }
        }
    }
}