using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

class Program
{
    // ---------- Állapot ----------
    static string inputPath;
    static string outputPath;
    static string[] mapLines;
    static List<Command> program;
    static Interpreter interpreter;

    static IEnumerator<int> activeRun;          // az aktív futás (nyomkövetés) – null, ha nincs
    static int nextLine;                        // a következőként végrehajtandó sor száma
    static int stepDelayMilliseconds = 250;     // késleltetés a lassított futásnál

    // ---------- Belépési pont ----------

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

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
            Console.WriteLine("Használat: robot [-q] input.txt output.txt");
            return 1;
        }

        inputPath = arguments[0];
        outputPath = arguments[1];
        Load();

        if (quietMode)
        {
            // Csendes mód: lefuttatja a programot, kiírja a naplót, nincs interaktív rész.
            interpreter.Reset();
            foreach (int executedLine in interpreter.Run(program))
            {
            }
            interpreter.Finish();
            WriteLog();
            return 0;
        }

        RunInteractiveLoop();
        return 0;
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
        File.WriteAllLines(outputPath, interpreter.Log);
    }

    // ---------- Futtatás ----------

    // Új futás indítása az alaphelyzetből (még egyetlen sort sem hajt végre).
    static void StartRun()
    {
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

    // Lassított futás: lépésenként újrarajzol, bármely billentyűre megszakítható.
    static void RunSlowly()
    {
        StartRun();
        while (AdvanceRun())
        {
            Redraw("Futtatás… (bármely billentyű: megszakítás)");
            Thread.Sleep(stepDelayMilliseconds);

            try
            {
                if (Console.KeyAvailable)
                {
                    Console.ReadKey(true);
                    activeRun = null;
                    WriteLog();
                    Console.WriteLine("Megszakítva.");
                    return;
                }
            }
            catch
            {
                // Ha a konzol nem interaktív, a billentyűfigyelés nem működik – ilyenkor nem szakítható meg.
            }
        }
        Redraw("A futás befejeződött.");
    }

    // ---------- Megjelenítés ----------

    static void Redraw(string status)
    {
        try
        {
            Console.Clear();
        }
        catch
        {
            // Ha a konzol nem törölhető (pl. átirányított kimenet), egyszerűen tovább írunk.
        }
        Show(status);
    }

    // Egy cella jele a térképen: robot (nyíl), felfedezett cella (valódi jel) vagy '?'.
    static char GetMapSymbol(int row, int column)
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
    static string DescribeFrontCell()
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

    static string DescribeVariables()
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

    // A konzolkimenet utolsó 15 sora.
    static string DescribeConsoleOutput()
    {
        if (interpreter.Output.Count == 0)
        {
            return "-";
        }

        int firstShownIndex = Math.Max(0, interpreter.Output.Count - 15);
        return string.Join(" | ", interpreter.Output.Skip(firstShownIndex));
    }

    static void Show(string status)
    {
        var text = new StringBuilder();

        text.AppendLine("=== SZIMULÁTOR  (?=feltérképezetlen  .=üres  #=fal  @=arany  > ^ < v = robot) ===");

        for (int row = 0; row < interpreter.Rows; row++)
        {
            for (int column = 0; column < interpreter.Columns; column++)
            {
                text.Append(GetMapSymbol(row, column)).Append(' ');
            }
            text.AppendLine();
        }

        text.AppendLine("Szenzor (a robot előtt): " + DescribeFrontCell() + "   |   Kibányászott arany: " + interpreter.CollectedGold);
        text.AppendLine("Változók: " + DescribeVariables());
        text.AppendLine("Konzol: " + DescribeConsoleOutput());

        if (interpreter.ErrorCode != null)
        {
            text.AppendLine("HIBA: " + interpreter.ErrorCode);
        }
        text.AppendLine("Állapot: " + status);

        Console.Write(text.ToString());
    }

    // A szöveges forráskód sorszámokkal; a következőként végrehajtandó sort "=>" jelöli.
    static void ShowText()
    {
        Language.AssignLineNumbers(program);
        List<string> sourceLines = Language.RenderSourceCode(program);

        Console.WriteLine("=== SZÖVEGES FORRÁSKÓD ===");
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

            Console.WriteLine(marker + lineNumber.ToString().PadLeft(3) + "  " + sourceLines[i]);
        }

        if (sourceLines.Count == 0)
        {
            Console.WriteLine("   (üres program)");
        }
    }

    static void ShowBlocks()
    {
        var blockLines = new List<string>();
        Language.BuildBlockView(program, "", 0, blockLines);

        Console.WriteLine("=== BLOKK-NÉZET (a címkék a szerkesztő pozíciói) ===");
        if (blockLines.Count == 0)
        {
            Console.WriteLine("(üres program – használd: add <blokk>)");
        }
        blockLines.ForEach(Console.WriteLine);
    }

    static void ShowHelp()
    {
        Console.WriteLine(@"PARANCSOK
 Nézetek:    text | blocks | map | log
 Szerkesztés (blokk-alapú, mindig érvényes szerkezet; a pozíciók a 'blocks' nézet címkéi, pl. 3, 3.2, 3.e.1):
   add <blokk>              új blokk a program végére
   ins <cím> <blokk>        beszúrás a címen lévő blokk ELÉ     (pl. ins 1 set x to 0)
   after <cím> <blokk>      beszúrás a címen lévő blokk MÖGÉ
   into <cím> <blokk>       beszúrás egy vezérlőblokk törzsének végére (else ág: into 2.e ...)
   edit <cím> <blokk>       blokk mezőinek átírása (azonos típusra)
   del <cím>                blokk törlése (a tartalmával együtt)
   save                     program mentése az input fájlba
   Blokkok: step forward X | turn by X | mine here | print X | set V to <X | isThere X | not X | X op Y>
            if X | ifelse X | while X      (X: változó, szám 0..100, true/false; op: + - * = <> < <= > >= and or)
 Futtatás:   run (lassított) | debug (nyomkövetés) | n [db] (következő lépés) | stop | speed <ms>
 Kézi vezérlés (megszakítja a futást, nincs naplózás): f [n] (előre) | l [n] (balra fordul) | m (bányász)
 Egyéb:      help | exit");
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
            Console.WriteLine("Nincs ilyen blokk.");
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
            Console.WriteLine("Hibás cím. Nézd meg a 'blocks' nézet címkéit.");
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
            Console.WriteLine("Nincs ilyen pozíció.");
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
            Console.WriteLine("Nincs ilyen blokk.");
            return false;
        }

        Command controlBlock = containingList[index];
        if (!controlBlock.IsControlBlock)
        {
            Console.WriteLine("Csak vezérlőblokk törzsébe lehet beszúrni.");
            return false;
        }
        if (intoElseBranch && controlBlock.ElseBody == null)
        {
            Console.WriteLine("Ennek az if-nek nincs else ága.");
            return false;
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
            Console.WriteLine(validationError);
            return false;
        }

        Command newBlock = Language.CreateCommand(tokens);
        if (newBlock.Type != existingBlock.Type)
        {
            Console.WriteLine("Az edit csak azonos típusú blokkra működik (töröld és szúrd be újra).");
            return false;
        }

        existingBlock.Arguments = newBlock.Arguments;
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
        string[] tokens = Tokenize(blockText);
        string validationError = Language.Validate(tokens);
        if (validationError != null)
        {
            Console.WriteLine(validationError);
            return false;
        }

        Command newBlock = Language.CreateCommand(tokens);
        if (newBlock.IsControlBlock && depth >= 3)
        {
            Console.WriteLine("Legfeljebb 3 szint mélyen ágyazhatók egymásba a vezérlőblokkok.");
            return false;
        }

        list.Insert(index, newBlock);
        return true;
    }

    // ---------- Fő ciklus (parancsok beolvasása) ----------

    static void RunInteractiveLoop()
    {
        Console.WriteLine("MineScript IDE – 'help' a parancsokért.");
        Redraw("Alaphelyzet.");
        ShowText();

        while (true)
        {
            Console.Write("\n> ");
            string inputLine = Console.ReadLine();
            if (inputLine == null)
            {
                return;
            }

            // Az első szó a parancs, a többi az argumentum.
            string[] inputParts = inputLine.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (inputParts.Length == 0)
            {
                continue;
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
                bool shouldExit = HandleCommand(command, argument);
                if (shouldExit)
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException)
            {
                Console.WriteLine("Hibás szám.");
            }
        }
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

            case "map":
                HandleMap();
                break;

            case "log":
                HandleLog();
                break;

            case "speed":
                stepDelayMilliseconds = Math.Max(0, int.Parse(argument));
                Console.WriteLine("Késleltetés: " + stepDelayMilliseconds + " ms");
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
                RunSlowly();
                break;

            case "debug":
                StartRun();
                AdvanceRun();
                Show("Nyomkövetés: a következő sor a ⇒ jelölt.");
                ShowText();
                break;

            case "n":
                HandleNextStep(argument);
                break;

            case "stop":
                activeRun = null;
                Console.WriteLine("Futás megszakítva (a robot állapota megmaradt).");
                break;

            case "f":
            case "l":
            case "m":
                HandleManualControl(command, argument);
                break;

            default:
                Console.WriteLine("Ismeretlen parancs. 'help'");
                break;
        }

        return false;
    }

    static void HandleMap()
    {
        if (activeRun != null)
        {
            Show("Nyomkövetés folyamatban.");
        }
        else
        {
            Show("Kész / alaphelyzet.");
        }
    }

    static void HandleLog()
    {
        if (File.Exists(outputPath))
        {
            Console.WriteLine(File.ReadAllText(outputPath));
        }
        else
        {
            Console.WriteLine("(még nincs napló)");
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
        Console.WriteLine("Mentve: " + inputPath);
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
        interpreter.Reset();
        Language.AssignLineNumbers(program);
        WriteLog();

        ShowBlocks();
        Console.WriteLine();
        ShowText();
    }

    // "n [db]": a nyomkövetésben a megadott számú (alapból 1) lépést előre megy.
    static void HandleNextStep(string argument)
    {
        if (activeRun == null)
        {
            Console.WriteLine("Nincs aktív nyomkövetés (debug).");
            return;
        }

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

        if (activeRun != null)
        {
            Show("Nyomkövetés: a következő sor a ⇒ jelölt.");
        }
        else
        {
            Show("A program véget ért.");
        }
        ShowText();
    }

    // "f [n]" / "l [n]" / "m": kézi vezérlés; a futó programot megszakítja.
    static void HandleManualControl(string command, string argument)
    {
        if (activeRun != null)
        {
            activeRun = null;
            Console.WriteLine("A programfutás megszakadt, kézi vezérlés.");
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
        Show("Kézi vezérlés: " + message);
    }
}