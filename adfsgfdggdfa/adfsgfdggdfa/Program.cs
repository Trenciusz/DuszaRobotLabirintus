using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;

class Program
{
    // ---------- Állapot ----------
    static string inputPath;
    static string outputPath;
    static string[] mapLines;
    static List<Command> program;
    public static Interpreter interpreter;

    static IEnumerator<int> activeRun;          // az aktív futás (nyomkövetés) – null, ha nincs
    static int nextLine;                        // a következőként végrehajtandó sor száma
    public static int stepDelayMilliseconds = 250;     // késleltetés a lassított futásnál

    // ---------- Belépési pont ----------

    // A parancssori paraméterek feldolgozása és az input fájl beolvasása (az App.xaml.cs hívja).
    // Hamis, ha kevés a paraméter (használat: robot [-q] input.txt output.txt).
    public static bool Initialize(string[] args)
    {
        bool quietMode = args.Length > 0 && args[0] == "-q";
        string[] arguments;
        if (quietMode)
        {
            arguments = args.Skip(1).ToArray();
        }
        else
        {
            arguments = args;
        }

        if (arguments.Length < 2)
        {
            return false;
        }

        QuietMode = quietMode;
        inputPath = arguments[0];
        outputPath = arguments[1];
        Load();
        return true;
    }

    // Csendes mód (-q): lefuttatja a programot, kiírja a naplót, nincs ablak és nincs interaktív rész.
    public static void RunQuiet()
    {
        interpreter.Reset();
        foreach (int executedLine in interpreter.Run(program))
        {
        }
        interpreter.Finish();
        WriteLog();
    }

    // ---------- Üzenetek és állapot az ablak számára ----------

    public static bool QuietMode;                           // csendes mód (-q): nincs ablak
    public static bool slowRunning;                         // igaz, ha a lassított futás (run) tart
    public static string StatusText = "Alaphelyzet.";       // az "Állapot" sor szövege
    static List<string> messages = new List<string>();      // a Command bar kimenetére szánt üzenetek

    // Üzenet a Command bar kimenetére (a konzolos változat kiírása helyett).
    static void Say(string text)
    {
        messages.Add(text);
    }

    // Az eddig összegyűlt üzenetek kivétele (az ablak hívja minden parancs után).
    public static List<string> TakeMessages()
    {
        List<string> result = new List<string>(messages);
        messages.Clear();
        return result;
    }

    // Beolvassa az input fájlt: az első sor a térkép mérete, utána a térkép, végül a program.
    static void Load()
    {
        List<string> fileLines = File.ReadAllLines(inputPath).Select(line => line.TrimEnd('\r')).ToList();

        string[] sizeParts = fileLines[0].Trim().Split(' ');
        int mapRowCount = int.Parse(sizeParts[0]);

        mapLines = fileLines.Skip(1).Take(mapRowCount).Select(line => line.TrimEnd()).ToArray();
        program = Language.ParseSource(fileLines.Skip(1 + mapRowCount));

        interpreter = new Interpreter(mapLines);
        Language.AssignLineNumbers(program);
    }

    static void WriteLog()
    {
        if (interpreter.Log.Count == 0)
        {
            File.WriteAllText(outputPath, "Az output üres.");
        }
        else
        {
            File.WriteAllLines(outputPath, interpreter.Log);
        }
    }

    // ---------- Futtatás ----------

    // Új futás indítása az alaphelyzetből (még egyetlen sort sem hajt végre).
    static void StartRun()
    {
        slowRunning = false;
        interpreter.Reset();
        Language.AssignLineNumbers(program);
        activeRun = interpreter.Run(program).GetEnumerator();
        nextLine = 0;
        WriteLog();
    }

    // Egy lépés előre a futásban. Igazat ad, ha a futás még tart.
    static bool AdvanceRun()
    {
        if (activeRun == null)
        {
            return false;
        }

        if (activeRun.MoveNext())
        {
            nextLine = activeRun.Current;
        }
        else
        {
            interpreter.Finish();
            activeRun = null;
            nextLine = 0;
        }

        WriteLog();
        return activeRun != null;
    }

    // A lassított futás (run) egy lépése. Az ablak időzítője hívja, amíg a futás tart.
    public static void SlowRunTick()
    {
        if (!slowRunning)
        {
            return;
        }

        if (activeRun == null)
        {
            slowRunning = false;
            return;
        }

        bool runContinues = AdvanceRun();
        if (runContinues)
        {
            SetStatus("Futtatás…");
        }
        else
        {
            slowRunning = false;
            SetStatus("A futás befejeződött.");
        }
    }

    // ---------- Megjelenítés (az ablak ezeket kérdezi le) ----------

    // Egy cella jele a térképen: robot (nyíl), felfedezett cella (valódi jel) vagy '?'.
    public static char GetMapSymbol(int row, int column)
    {
        bool isRobotHere = row == interpreter.RobotRow && column == interpreter.RobotColumn;
        if (isRobotHere)
        {
            return Interpreter.RobotArrows[interpreter.Direction];
        }

        if (interpreter.Explored[row, column])
        {
            return interpreter.Grid[row, column];
        }
        return '?';
    }

    // A robot előtti cella szöveges leírása.
    public static string DescribeFrontCell()
    {
        switch (interpreter.GetFrontCellType())
        {
            case Interpreter.CellWall:
                return "fal";
            case Interpreter.CellGold:
                return "aranyrög";
            default:
                return "üres kamra";
        }
    }

    public static string DescribeVariables()
    {
        if (interpreter.Variables.Count == 0)
        {
            return "-";
        }

        var parts = new List<string>();
        foreach (KeyValuePair<string, object> variable in interpreter.Variables)
        {
            parts.Add(variable.Key + "=" + Interpreter.FormatValue(variable.Value));
        }
        return string.Join(", ", parts);
    }

    // A konzolkimenet: az összes eddigi print, " | " jellel elválasztva.
    public static string DescribeConsoleOutput()
    {
        if (interpreter.Output.Count == 0)
        {
            return "-";
        }

        return string.Join(" | ", interpreter.Output);
    }

    // Az "Állapot" sor szövegének beállítása (az ablak ezt jeleníti meg).
    static void SetStatus(string status)
    {
        StatusText = status;
    }

    // A nyomkövetés állapotsora: tart még a futás, vagy véget ért.
    static void ShowDebugStatus()
    {
        if (activeRun != null)
        {
            SetStatus("Nyomkövetés: a következő sor a => jelölt.");
        }
        else
        {
            SetStatus("A program véget ért.");
        }
    }

    // A szöveges forráskód sorai sorszámokkal; a következőként végrehajtandó sort "=>" jelöli.
    public static List<string> GetTextViewLines()
    {
        Language.AssignLineNumbers(program);
        List<string> sourceLines = Language.RenderSourceCode(program);
        var viewLines = new List<string>();

        for (int i = 0; i < sourceLines.Count; i++)
        {
            int lineNumber = i + 1;
            bool isNextLine = lineNumber == nextLine && activeRun != null;

            string marker;
            if (isNextLine)
            {
                marker = "=> ";
            }
            else
            {
                marker = "   ";
            }

            viewLines.Add(marker + lineNumber.ToString().PadLeft(3) + "  " + sourceLines[i]);
        }

        if (sourceLines.Count == 0)
        {
            viewLines.Add("   (üres program)");
        }
        return viewLines;
    }

    static void ShowText()
    {
        Say("=== SZÖVEGES FORRÁSKÓD ===");
        foreach (string viewLine in GetTextViewLines())
        {
            Say(viewLine);
        }
    }

    // A blokk-nézet sorai; a címkék a szerkesztő pozíciói.
    public static List<string> GetBlockViewLines()
    {
        var blockLines = new List<string>();
        Language.BuildBlockView(program, "", 0, blockLines);

        if (blockLines.Count == 0)
        {
            blockLines.Add("(üres program – használd: add <blokk>)");
        }
        return blockLines;
    }

    static void ShowBlocks()
    {
        Say("=== BLOKK-NÉZET (a címkék a szerkesztő pozíciói) ===");
        foreach (string blockLine in GetBlockViewLines())
        {
            Say(blockLine);
        }
    }


    static void ShowHelp()
    {
        MessageBox.Show(
           @"========== PARANCSOK ==========

NÉZETEK
  text | blocks | log
  A program szöveges és blokk nézete mindig látszik középen.

========== SZERKESZTÉS ==========

  A szerkesztés blokk-alapú, mindig érvényes szerkezetet tart fenn.
  A pozíciók a 'blocks' nézet címkéi (pl. 3, 3.2, 3.e.1).

  add <blokk>             Új blokk a program végére
  ins <cím> <blokk>       Beszúrás a címen lévő blokk ELÉ
                          (pl. ins 1 set x to 0)
  after <cím> <blokk>    Beszúrás a címen lévő blokk MÖGÉ
  into <cím> <blokk>     Beszúrás egy vezérlőblokk törzsének végére
                          (else ág: into 2.e ...)
  after <cím> else       Else ág hozzáadása a címen lévő if blokkhoz
                          (utána: into <cím>.e <blokk>)
  edit <cím> <blokk>     Blokk mezőinek átírása (azonos típusra)
  del <cím>              Blokk törlése (a tartalmával együtt)
  save                   Program mentése az input fájlba

========== BLOKKOK ==========

  step forward X
  turn by X
  mine here
  print X
  set V to X
  isThere X
  not X
  X op Y

  if X | ifelse X | while X

  X: változó, 0..100 közötti szám vagy true/false
  op: + - * = <> < <= > >= and or

========== FUTTATÁS ==========

  run                    Lassított futtatás
  debug                  Nyomkövetés
  n [db]                 Következő lépés
  stop                   Futtatás leállítása
  speed <ms>             Futtatási sebesség beállítása

========== KÉZI VEZÉRLÉS ==========

  f [n]                  Előre lépés
  l [n]                  Balra fordulás
  m                      Bányászat

  A kézi vezérlés megszakítja a futást, nincs naplózás.

========== EGYÉB ==========

  help                   Súgó megjelenítése
  exit                   Kilépés

========== GOMBOK ==========

  help  = help
  Run   = run
  Debug = debug
  Next  = n
  Stop  = stop", "",
           MessageBoxButton.OK,
           MessageBoxImage.Information
       );
    }
    // ---------- Szerkesztés ----------

    // Megkeresi a címnek (pl. "3.2.e.1") megfelelő pozíciót a programban.
    //   containingList: az a lista, amelyben a cím szerinti pozíció van
    //   index:          a pozíció indexe ebben a listában (lehet a lista hossza is: "a végére")
    //   depth:          hány vezérlőblokk (if / while) veszi körül a listát
    // Hamis, ha a cím érvénytelen.
    static bool TryLocate(string path, out List<Command> containingList, out int index, out int depth)
    {
        containingList = program;
        index = 0;
        depth = 0;

        string[] pathParts = path.Split('.');
        Command parentCommand = null;

        // Igaz, ha a containingList már a megfelelő lista a következő számhoz;
        // hamis, ha előbb a parentCommand törzsébe kell lépni.
        bool listIsReady = true;

        for (int k = 0; k < pathParts.Length; k++)
        {
            bool isLastPart = k == pathParts.Length - 1;

            if (pathParts[k] == "e")
            {
                // "e": az előző blokk else ágába lépünk (utána még kell egy sorszám)
                if (parentCommand == null || parentCommand.ElseBody == null || isLastPart)
                {
                    return false;
                }
                containingList = parentCommand.ElseBody;
                listIsReady = true;
                continue;
            }

            if (!listIsReady)
            {
                // Az előző blokk törzsébe lépünk
                if (!parentCommand.IsControlBlock)
                {
                    return false;
                }
                containingList = parentCommand.Body;
            }

            index = int.Parse(pathParts[k]) - 1;
            if (index < 0)
            {
                return false;
            }

            if (!isLastPart)
            {
                if (index >= containingList.Count)
                {
                    return false;
                }
                parentCommand = containingList[index];
                listIsReady = false;
                depth++;
            }
        }

        return index <= containingList.Count;
    }

    // Kiírja a hibát és hamisat ad, ha a listában nincs ilyen indexű blokk.
    static bool BlockExists(List<Command> list, int index)
    {
        if (index >= list.Count)
        {
            Say("Nincs ilyen blokk.");
            return false;
        }
        return true;
    }

    // Szerkesztőparancs végrehajtása (add / ins / after / into / edit / del).
    // Igaz, ha a program megváltozott.
    static bool EditProgram(string command, string argument)
    {
        if (command == "add")
        {
            return InsertBlock(program, program.Count, 0, argument);
        }

        // A többi parancs "<cím> [blokk]" alakú argumentumot vár.
        Match addressMatch = Regex.Match(argument, @"^(\d+(?:\.(?:\d+|e))*)(?:\s+(.*))?$");
        if (!addressMatch.Success)
        {
            Say("Hibás cím. Nézd meg a 'blocks' nézet címkéit.");
            return false;
        }
        string path = addressMatch.Groups[1].Value;
        string blockText = addressMatch.Groups[2].Value;

        if (command == "into")
        {
            return InsertIntoControlBlock(path, blockText);
        }

        List<Command> containingList;
        int index;
        int depth;
        if (!TryLocate(path, out containingList, out index, out depth))
        {
            Say("Nincs ilyen pozíció.");
            return false;
        }

        switch (command)
        {
            case "ins":
                return InsertBlock(containingList, index, depth, blockText);

            case "after":
                if (!BlockExists(containingList, index))
                {
                    return false;
                }
                if (blockText.Trim() == "else")
                {
                    return AddElseBranch(containingList[index]);
                }
                return InsertBlock(containingList, index + 1, depth, blockText);

            case "del":
                if (!BlockExists(containingList, index))
                {
                    return false;
                }
                containingList.RemoveAt(index);
                return true;

            case "edit":
                if (!BlockExists(containingList, index))
                {
                    return false;
                }
                return ReplaceBlockFields(containingList[index], blockText);
        }

        return false;
    }

    // "into <cím> <blokk>": beszúrás egy vezérlőblokk törzsének (vagy else ágának) végére.
    static bool InsertIntoControlBlock(string path, string blockText)
    {
        bool intoElseBranch = path.EndsWith(".e");

        string controlBlockPath;
        if (intoElseBranch)
        {
            controlBlockPath = path.Substring(0, path.Length - 2);
        }
        else
        {
            controlBlockPath = path;
        }

        List<Command> containingList;
        int index;
        int depth;
        if (!TryLocate(controlBlockPath, out containingList, out index, out depth) || index >= containingList.Count)
        {
            Say("Nincs ilyen blokk.");
            return false;
        }

        Command controlBlock = containingList[index];
        if (!controlBlock.IsControlBlock)
        {
            Say("Csak vezérlőblokk törzsébe lehet beszúrni.");
            return false;
        }
        if (intoElseBranch && controlBlock.ElseBody == null)
        {
            Say("Ennek az if-nek nincs else ága.");
            return false;
        }

        if (blockText.Trim() == "else")
        {
            return AddElseBranch(controlBlock);
        }

        List<Command> targetBody;
        if (intoElseBranch)
        {
            targetBody = controlBlock.ElseBody;
        }
        else
        {
            targetBody = controlBlock.Body;
        }

        // A törzs eggyel mélyebben van, mint a vezérlőblokk maga.
        return InsertBlock(targetBody, targetBody.Count, depth + 1, blockText);
    }

    // "edit <cím> <blokk>": a meglévő blokk mezőit írja át (a típus nem változhat).
    static bool ReplaceBlockFields(Command existingBlock, string blockText)
    {
        string[] tokens = Tokenize(blockText);
        string validationError = Language.Validate(tokens);
        if (validationError != null)
        {
            Say(validationError);
            return false;
        }

        Command newBlock = Language.CreateCommand(tokens);
        if (newBlock.Type != existingBlock.Type)
        {
            Say("Az edit csak azonos típusú blokkra működik (töröld és szúrd be újra).");
            return false;
        }

        existingBlock.Arguments = newBlock.Arguments;
        return true;
    }

    // "after <cím> else" / "into <cím> else": a címen lévő else nélküli if blokkot if-else blokká alakítja
    // (üres else ággal). Az else ág tartalmát utána az "into <cím>.e <blokk>" paranccsal lehet felépíteni.
    static bool AddElseBranch(Command targetBlock)
    {
        if (targetBlock.Type != CommandType.If)
        {
            Say("Else ágat csak if blokkhoz lehet adni. Az if blokk címét add meg, pl.: after 3.2 else");
            return false;
        }

        if (targetBlock.ElseBody != null)
        {
            Say("Ennek az if-nek már van else ága.");
            return false;
        }

        targetBlock.ElseBody = new List<Command>();
        return true;
    }

    static string[] Tokenize(string text)
    {
        if (text == null)
        {
            text = "";
        }
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    // Új blokk beszúrása a listába az adott indexre.
    // depth = a beszúrási hely körüli vezérlőblokkok száma; új vezérlőblokk csak akkor szúrható be,
    // ha depth <= 2 (legfeljebb 3 szint mélyen ágyazhatók egymásba).
    static bool InsertBlock(List<Command> list, int index, int depth, string blockText)
    {
        if (blockText.Trim() == "else")
        {
            Say("Az else-t az if blokk címével add meg, pl.: after 3.2 else");
            return false;
        }

        string[] tokens = Tokenize(blockText);
        string validationError = Language.Validate(tokens);
        if (validationError != null)
        {
            Say(validationError);
            return false;
        }

        Command newBlock = Language.CreateCommand(tokens);
        if (newBlock.IsControlBlock && depth >= 3)
        {
            Say("Legfeljebb 3 szint mélyen ágyazhatók egymásba a vezérlőblokkok.");
            return false;
        }

        list.Insert(index, newBlock);
        return true;
    }

    // ---------- Fő ciklus (parancsok beolvasása) ----------

    // Egy beírt sor feldolgozása (a Command bar és a gombok hívják).
    // Az első szó a parancs, a többi az argumentum. Igazat ad, ha ki kell lépni.
    public static bool ExecuteInput(string inputLine)
    {
        string[] inputParts = inputLine.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (inputParts.Length == 0)
        {
            return false;
        }

        string command = inputParts[0].ToLower();
        string argument;
        if (inputParts.Length > 1)
        {
            argument = inputParts[1];
        }
        else
        {
            argument = "";
        }

        try
        {
            return HandleCommand(command, argument);
        }
        catch (Exception exception) when (exception is FormatException || exception is OverflowException)
        {
            Say("Hibás szám.");
        }

        return false;
    }

    // Egy parancs feldolgozása. Igazat ad, ha ki kell lépni a programból.
    static bool HandleCommand(string command, string argument)
    {
        switch (command)
        {
            case "help":
                ShowHelp();
                break;

            case "exit":
                return true;

            case "text":
                ShowText();
                break;

            case "blocks":
                ShowBlocks();
                break;

            case "log":
                HandleLog();
                break;

            case "speed":
                stepDelayMilliseconds = Math.Max(0, int.Parse(argument));
                Say("Késleltetés: " + stepDelayMilliseconds + " ms");
                break;

            case "save":
                HandleSave();
                break;

            case "add":
            case "ins":
            case "after":
            case "into":
            case "edit":
            case "del":
                HandleEditCommand(command, argument);
                break;

            case "run":
                StartRun();
                slowRunning = true;
                SetStatus("Futtatás… (lassított, a Stop gombbal megszakítható)");
                break;

            case "debug":
                StartRun();
                AdvanceRun();
                ShowDebugStatus();
                break;

            case "n":
                HandleNextStep(argument);
                break;

            case "stop":
                activeRun = null;
                slowRunning = false;
                SetStatus("Megszakítva.");
                Say("Futás megszakítva (a robot állapota megmaradt).");
                break;

            case "f":
            case "l":
            case "m":
                HandleManualControl(command, argument);
                break;

            default:
                Say("Ismeretlen parancs. 'help'");
                break;
        }

        return false;
    }

    static void HandleLog()
    {
        if (File.Exists(outputPath))
        {
            Say(File.ReadAllText(outputPath));
        }
        else
        {
            Say("(még nincs napló)");
        }
    }

    // A program mentése az input fájlba: méret, térkép, majd a program szöveges alakja.
    static void HandleSave()
    {
        string sizeLine = interpreter.Rows + " " + interpreter.Columns;
        IEnumerable<string> fileContent = new[] { sizeLine }
            .Concat(mapLines)
            .Concat(Language.RenderSourceCode(program));

        File.WriteAllLines(inputPath, fileContent);
        Say("Mentve: " + inputPath);
    }

    // Szerkesztés után a futás elölről indul: alaphelyzet, új sorszámok, friss napló és nézetek.
    static void HandleEditCommand(string command, string argument)
    {
        bool programChanged = EditProgram(command, argument);
        if (!programChanged)
        {
            return;
        }

        activeRun = null;
        slowRunning = false;
        interpreter.Reset();
        Language.AssignLineNumbers(program);
        WriteLog();

        SetStatus("A program módosult, alaphelyzet.");
        Say("A program módosult, a futás alaphelyzetbe állt.");
    }

    // "n [db]": a nyomkövetésben a megadott számú (alapból 1) lépést előre megy.
    static void HandleNextStep(string argument)
    {
        if (activeRun == null)
        {
            Say("Nincs aktív nyomkövetés (debug).");
            return;
        }

        slowRunning = false;

        int stepCount;
        if (argument == "")
        {
            stepCount = 1;
        }
        else
        {
            stepCount = int.Parse(argument);
        }

        for (int i = 0; i < stepCount; i++)
        {
            if (!AdvanceRun())
            {
                break;
            }
        }

        ShowDebugStatus();
    }

    // "f [n]" / "l [n]" / "m": kézi vezérlés; a futó programot megszakítja.
    static void HandleManualControl(string command, string argument)
    {
        if (activeRun != null)
        {
            activeRun = null;
            slowRunning = false;
            Say("A programfutás megszakadt, kézi vezérlés.");
        }

        int amount;
        if (argument == "")
        {
            amount = 1;
        }
        else
        {
            amount = int.Parse(argument);
        }

        string message = interpreter.ManualControl(command, amount);
        SetStatus("Kézi vezérlés: " + message);
    }
}