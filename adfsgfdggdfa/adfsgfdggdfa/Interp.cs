using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

// A futtatás során fellépő hiba. Az ErrorCode kerül be a naplóba (pl. "E_BAD_ARG").
class InterpreterException : Exception
{
    public string ErrorCode;

    public InterpreterException(string errorCode)
    {
        ErrorCode = errorCode;
    }
}

class Interpreter
{
    // ---------- Irányok ----------
    // Index: 0 = kelet, 1 = észak, 2 = nyugat, 3 = dél.
    // Balra fordulás = index + 1.
    static readonly int[] RowChangePerDirection = { 0, -1, 0, 1 };
    static readonly int[] ColumnChangePerDirection = { 1, 0, -1, 0 };

    // A robotot jelölő nyilak a térképen, az irány indexének sorrendjében.
    public const string RobotArrows = ">^<v";

    // ---------- Cellatípusok (a szenzor ezeket adja vissza) ----------
    public const int CellEmpty = 0;
    public const int CellWall = 1;
    public const int CellGold = 2;

    const int MaxInstructionCount = 1000;

    // ---------- Állapot ----------
    string[] initialMap;                  // a kezdő térkép (Reset ebből építi újra a rácsot)
    public int Rows;
    public int Columns;
    public char[,] Grid;                  // a térkép jelenlegi állapota ('#', '@', '.')
    public bool[,] Explored;              // melyik cellát látta/érintette már a robot
    public int RobotRow;
    public int RobotColumn;
    public int Direction;                 // 0..3, lásd fent
    public int CollectedGold;
    public int InstructionCount;          // eddig végrehajtott utasítások száma

    public Dictionary<string, object> Variables = new Dictionary<string, object>();   // értékek: int vagy bool
    public List<string> Log = new List<string>();      // az output fájl tartalma
    public List<string> Output = new List<string>();   // a print utasítások kimenete

    public bool Stopped;                  // a futás leállt (hiba vagy limit miatt)
    public bool LimitExceeded;            // az utasításszám-limit miatt állt le
    public bool Finished;                 // a futás véget ért (Finish hívás után)
    public string ErrorCode;              // az utolsó hiba kódja, ha volt

    // Az aktuális sor "API-hívásának" leírása a naplóhoz (pl. "step forward 3"); null, ha nincs.
    string currentApiCall;

    public Interpreter(string[] map)
    {
        initialMap = map;
        Rows = map.Length;
        Columns = map[0].Length;
        Reset();
    }

    // Visszaállítja az alaphelyzetet: térkép, robot, változók, napló.
    public void Reset()
    {
        Grid = new char[Rows, Columns];
        Explored = new bool[Rows, Columns];

        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                char symbol = initialMap[row][column];

                // Ha nyilat találunk, ott áll a robot; a cella maga üres.
                int arrowDirection = RobotArrows.IndexOf(symbol);
                if (arrowDirection >= 0)
                {
                    RobotRow = row;
                    RobotColumn = column;
                    Direction = arrowDirection;
                    symbol = '.';
                }

                Grid[row, column] = symbol;
            }
        }

        Explored[RobotRow, RobotColumn] = true;

        Variables.Clear();
        Log.Clear();
        Output.Clear();

        CollectedGold = 0;
        InstructionCount = 0;
        Stopped = false;
        LimitExceeded = false;
        Finished = false;
        ErrorCode = null;
    }

    // ---------- Térkép és szenzor ----------

    // A cella típusa. A térképen kívüli cella falnak számít.
    public int GetCellType(int row, int column)
    {
        bool isOutsideMap = row < 0 || column < 0 || row >= Rows || column >= Columns;
        if (isOutsideMap)
        {
            return CellWall;
        }

        char symbol = Grid[row, column];
        if (symbol == '#')
        {
            return CellWall;
        }
        if (symbol == '@')
        {
            return CellGold;
        }
        return CellEmpty;
    }

    // A robot előtti cella típusa.
    public int GetFrontCellType()
    {
        int frontRow = RobotRow + RowChangePerDirection[Direction];
        int frontColumn = RobotColumn + ColumnChangePerDirection[Direction];
        return GetCellType(frontRow, frontColumn);
    }

    // A robot előtti cella felfedezetté tétele (ha a térképen belül van).
    // Falnak ütközéskor hívjuk, hogy a fal is látszódjon a térképen.
    void ExploreFrontCell()
    {
        int frontRow = RobotRow + RowChangePerDirection[Direction];
        int frontColumn = RobotColumn + ColumnChangePerDirection[Direction];
        bool isFrontInsideMap = frontRow >= 0 && frontColumn >= 0 && frontRow < Rows && frontColumn < Columns;
        if (isFrontInsideMap)
        {
            Explored[frontRow, frontColumn] = true;
        }
    }

    // Érték szöveggé alakítása a naplóhoz és a kimenethez (bool: "true"/"false").
    public static string FormatValue(object value)
    {
        if (value is bool)
        {
            if ((bool)value)
            {
                return "true";
            }
            return "false";
        }
        return value.ToString();
    }

    // ---------- Értékek és kifejezések ----------

    // Egy token értéke: true/false, szám vagy változó.
    object ReadValue(string token)
    {
        if (token == "true")
        {
            return true;
        }
        if (token == "false")
        {
            return false;
        }
        if (char.IsDigit(token[0]))
        {
            return int.Parse(token);
        }

        object variableValue;
        if (Variables.TryGetValue(token, out variableValue))
        {
            return variableValue;
        }
        throw new InterpreterException("E_UNINITIALIZED");
    }

    // Egy kifejezés kiértékelése (a "set" jobb oldala, illetve az if / while feltétele). Az eredmény int vagy bool.
    object EvaluateExpression(string[] expression)
    {
        if (expression.Length == 1)
        {
            return ReadValue(expression[0]);
        }
        if (expression[0] == "isThere")
        {
            return EvaluateIsThere(expression[1]);
        }
        if (expression[0] == "not")
        {
            return EvaluateNot(expression[1]);
        }

        string leftToken = expression[0];
        string operatorSymbol = expression[1];
        string rightToken = expression[2];

        if (operatorSymbol == "and" || operatorSymbol == "or")
        {
            return EvaluateLogicalOperation(leftToken, operatorSymbol, rightToken);
        }
        if (operatorSymbol == "=" || operatorSymbol == "<>")
        {
            return EvaluateEquality(leftToken, operatorSymbol, rightToken);
        }
        return EvaluateNumberOperation(leftToken, operatorSymbol, rightToken);
    }

    // isThere X: a robot előtti cella típusa egyenlő-e X-szel (0 = üres, 1 = fal, 2 = arany).
    // Mellékhatás: a robot előtti cella "felfedezetté" válik.
    object EvaluateIsThere(string argumentToken)
    {
        currentApiCall = "isThere";
        object argument = ReadValue(argumentToken);
        currentApiCall += " " + FormatValue(argument);

        bool isValidCellType = argument is int && (int)argument >= 0 && (int)argument <= 2;
        if (!isValidCellType)
        {
            throw new InterpreterException("E_BAD_ARG");
        }
        int expectedCellType = (int)argument;

        int frontRow = RobotRow + RowChangePerDirection[Direction];
        int frontColumn = RobotColumn + ColumnChangePerDirection[Direction];
        bool isFrontInsideMap = frontRow >= 0 && frontColumn >= 0 && frontRow < Rows && frontColumn < Columns;
        if (isFrontInsideMap)
        {
            Explored[frontRow, frontColumn] = true;
        }

        bool result = GetFrontCellType() == expectedCellType;
        currentApiCall += " " + FormatValue(result);
        return result;
    }

    // not X: logikai tagadás.
    object EvaluateNot(string operandToken)
    {
        object operand = ReadValue(operandToken);
        if (!(operand is bool))
        {
            throw new InterpreterException("E_BAD_OPERAND");
        }
        return !(bool)operand;
    }

    // and / or: mindkét oldalnak bool-nak kell lennie.
    object EvaluateLogicalOperation(string leftToken, string operatorSymbol, string rightToken)
    {
        object left = ReadValue(leftToken);
        if (!(left is bool))
        {
            throw new InterpreterException("E_BAD_OPERAND");
        }

        object right = ReadValue(rightToken);
        if (!(right is bool))
        {
            throw new InterpreterException("E_BAD_OPERAND");
        }

        bool leftBool = (bool)left;
        bool rightBool = (bool)right;
        if (operatorSymbol == "and")
        {
            return leftBool && rightBool;
        }
        return leftBool || rightBool;
    }

    // = / <>: a két oldalnak azonos típusúnak kell lennie (int–int vagy bool–bool).
    object EvaluateEquality(string leftToken, string operatorSymbol, string rightToken)
    {
        object left = ReadValue(leftToken);
        object right = ReadValue(rightToken);
        if (left.GetType() != right.GetType())
        {
            throw new InterpreterException("E_BAD_OPERAND");
        }

        bool areEqual = left.Equals(right);
        if (operatorSymbol == "=")
        {
            return areEqual;
        }
        return !areEqual;
    }

    // + - * < <= > >=: mindkét oldalnak számnak kell lennie.
    object EvaluateNumberOperation(string leftToken, string operatorSymbol, string rightToken)
    {
        object left = ReadValue(leftToken);
        if (!(left is int))
        {
            throw new InterpreterException("E_BAD_OPERAND");
        }

        object right = ReadValue(rightToken);
        if (!(right is int))
        {
            throw new InterpreterException("E_BAD_OPERAND");
        }

        int leftNumber = (int)left;
        int rightNumber = (int)right;

        switch (operatorSymbol)
        {
            case "+": return unchecked(leftNumber + rightNumber);
            case "-": return unchecked(leftNumber - rightNumber);
            case "*": return unchecked(leftNumber * rightNumber);
            case "<": return leftNumber < rightNumber;
            case "<=": return leftNumber <= rightNumber;
            case ">": return leftNumber > rightNumber;
            default: return leftNumber >= rightNumber;   // ">="
        }
    }

    // ---------- Egy sor végrehajtása ----------

    // Végrehajt egy sort és bejegyzi a naplóba (sorszám, API-hívás, esetleges hibakód).
    // Hiba esetén leállítja a futást.
    // Visszatérési érték: if / while esetén a feltétel értéke, egyébként false.
    bool ExecuteLine(Command command)
    {
        currentApiCall = null;
        string errorCodeOfThisLine = null;
        bool conditionValue = false;

        try
        {
            switch (command.Type)
            {
                case CommandType.Step:
                    ExecuteStep(command);
                    break;
                case CommandType.Turn:
                    ExecuteTurn(command);
                    break;
                case CommandType.Mine:
                    ExecuteMine();
                    break;
                case CommandType.Print:
                    ExecutePrint(command);
                    break;
                case CommandType.Set:
                    ExecuteSet(command);
                    break;
                default:
                    // if / while: a feltétel kiértékelése
                    conditionValue = EvaluateCondition(command);
                    break;
            }
        }
        catch (InterpreterException exception)
        {
            errorCodeOfThisLine = exception.ErrorCode;
        }

        string logLine = command.LineNumber.ToString();
        if (currentApiCall != null)
        {
            logLine += " " + currentApiCall;
        }
        if (errorCodeOfThisLine != null)
        {
            logLine += " " + errorCodeOfThisLine;
        }
        Log.Add(logLine);

        if (errorCodeOfThisLine != null)
        {
            Stopped = true;
            ErrorCode = errorCodeOfThisLine;
        }

        return conditionValue;
    }

    // step forward N: N lépés előre; falnak ütközve hiba.
    void ExecuteStep(Command command)
    {
        currentApiCall = "step forward";
        object argument = ReadValue(command.Arguments[0]);
        currentApiCall += " " + FormatValue(argument);

        if (!(argument is int) || (int)argument < 0)
        {
            throw new InterpreterException("E_BAD_ARG");
        }
        int stepCount = (int)argument;

        for (int i = 0; i < stepCount; i++)
        {
            if (GetFrontCellType() == CellWall)
            {
                ExploreFrontCell();
                throw new InterpreterException("E_BEDROCK_HIT");
            }
            RobotRow += RowChangePerDirection[Direction];
            RobotColumn += ColumnChangePerDirection[Direction];
            Explored[RobotRow, RobotColumn] = true;
        }
    }

    // turn by N: N-szer fordul balra (negyedfordulat).
    void ExecuteTurn(Command command)
    {
        currentApiCall = "turn by";
        object argument = ReadValue(command.Arguments[0]);
        currentApiCall += " " + FormatValue(argument);

        if (!(argument is int) || (int)argument < 0)
        {
            throw new InterpreterException("E_BAD_ARG");
        }
        int quarterTurns = (int)argument;

        Direction = (Direction + quarterTurns % 4) % 4;
    }

    // mine here: a robot alatti aranyat kibányássza.
    void ExecuteMine()
    {
        currentApiCall = "mine here";
        if (GetCellType(RobotRow, RobotColumn) != CellGold)
        {
            throw new InterpreterException("E_NO_GOLD");
        }
        Grid[RobotRow, RobotColumn] = '.';
        CollectedGold++;
    }

    // print X: az érték a konzolkimenetre kerül.
    void ExecutePrint(Command command)
    {
        currentApiCall = "print";
        object value = ReadValue(command.Arguments[0]);
        currentApiCall += " " + FormatValue(value);
        Output.Add(FormatValue(value));
    }

    // set V to <kifejezés>: a változó típusa nem változhat meg egy értékadással.
    void ExecuteSet(Command command)
    {
        string variableName = command.Arguments[0];
        string[] expression = command.Arguments.Skip(1).ToArray();

        object newValue = EvaluateExpression(expression);

        object oldValue;
        if (Variables.TryGetValue(variableName, out oldValue) && oldValue.GetType() != newValue.GetType())
        {
            throw new InterpreterException("E_TYPE_MISMATCH");
        }
        Variables[variableName] = newValue;
    }

    // if / while feltétele: tetszőleges kifejezés (érték, isThere x, not x, x művelet y),
    // de az eredménye bool értéknek kell lennie.
    bool EvaluateCondition(Command command)
    {
        object value = EvaluateExpression(command.Arguments);
        if (!(value is bool))
        {
            throw new InterpreterException("E_NON_BOOL_CONDITION");
        }
        return (bool)value;
    }

    // ---------- Utasításszám-limit és a futás lezárása ----------

    // Utasítás végrehajtása előtt hívandó. Hamis, ha elértük a limitet (ilyenkor a futás leáll).
    bool TryCountInstruction()
    {
        if (InstructionCount >= MaxInstructionCount)
        {
            Log.Add("INSTRUCTION_COUNT_LIMIT_EXCEEDED");
            Stopped = true;
            LimitExceeded = true;
            ErrorCode = "INSTRUCTION_COUNT_LIMIT_EXCEEDED";
            return false;
        }
        InstructionCount++;
        return true;
    }

    // A futás normál végén hívandó: "EOR" kerül a naplóba (limit miatti leállásnál nem).
    public void Finish()
    {
        if (!LimitExceeded)
        {
            Log.Add("EOR");
        }
        Finished = true;
    }

    // ---------- Végrehajtás (generátorokkal) ----------

    // A program futtatása generátorként: minden utasítás VÉGREHAJTÁSA ELŐTT
    // visszaadja a végrehajtandó sor számát (nyomkövetéshez).
    public IEnumerable<int> Run(List<Command> program)
    {
        Language.AssignLineNumbers(program);
        return ExecuteBlock(program);
    }

    // Blokk (utasítássorozat) végrehajtása, sorról sorra.
    IEnumerable<int> ExecuteBlock(List<Command> commands)
    {
        foreach (Command command in commands)
        {
            foreach (int lineNumber in ExecuteCommand(command))
            {
                yield return lineNumber;
            }

            if (Stopped)
            {
                yield break;
            }
        }
    }

    IEnumerable<int> ExecuteCommand(Command command)
    {
        if (command.Type == CommandType.While)
        {
            return ExecuteWhile(command);
        }
        if (command.Type == CommandType.If)
        {
            return ExecuteIf(command);
        }
        return ExecuteSimpleCommand(command);
    }

    // step / turn / mine / print / set
    IEnumerable<int> ExecuteSimpleCommand(Command command)
    {
        yield return command.LineNumber;

        if (!TryCountInstruction())
        {
            yield break;
        }
        ExecuteLine(command);
    }

    // while: a feltétel minden körben újra kiértékelődik (és újra számít utasításnak).
    IEnumerable<int> ExecuteWhile(Command command)
    {
        while (true)
        {
            yield return command.LineNumber;

            if (!TryCountInstruction())
            {
                yield break;
            }

            bool condition = ExecuteLine(command);
            if (Stopped)
            {
                yield break;
            }
            if (!condition)
            {
                yield break;
            }

            foreach (int lineNumber in ExecuteBlock(command.Body))
            {
                yield return lineNumber;
            }
            if (Stopped)
            {
                yield break;
            }
        }
    }

    // if: igaz feltételnél a törzs fut, hamisnál (ha van) az else ág.
    IEnumerable<int> ExecuteIf(Command command)
    {
        yield return command.LineNumber;

        if (!TryCountInstruction())
        {
            yield break;
        }

        bool condition = ExecuteLine(command);
        if (Stopped)
        {
            yield break;
        }

        if (condition)
        {
            foreach (int lineNumber in ExecuteBlock(command.Body))
            {
                yield return lineNumber;
            }
        }
        else if (command.ElseBody != null)
        {
            // Az "else" sor is utasításnak számít, és külön naplósor is jár neki.
            yield return command.ElseLineNumber;

            if (!TryCountInstruction())
            {
                yield break;
            }
            Log.Add(command.ElseLineNumber.ToString());

            foreach (int lineNumber in ExecuteBlock(command.ElseBody))
            {
                yield return lineNumber;
            }
        }
    }

    // ---------- Kézi vezérlés ----------

    // Kézi vezérlés: nincs naplózás, falnak ütközésnél hiba nélkül megáll.
    // command: "f" = előre, "l" = balra fordul, egyéb = bányászik. Üzenettel tér vissza.
    public string ManualControl(string command, int amount)
    {
        switch (command)
        {
            case "f":
                for (int i = 0; i < amount; i++)
                {
                    if (GetFrontCellType() == CellWall)
                    {
                        ExploreFrontCell();
                        return "Falnak ütköztél, a robot megállt.";
                    }
                    RobotRow += RowChangePerDirection[Direction];
                    RobotColumn += ColumnChangePerDirection[Direction];
                    Explored[RobotRow, RobotColumn] = true;
                }
                return "Lépés kész.";

            case "l":
                Direction = (Direction + amount % 4) % 4;
                return "Fordulás kész.";

            default:
                if (GetCellType(RobotRow, RobotColumn) != CellGold)
                {
                    return "Itt nincs arany.";
                }
                Grid[RobotRow, RobotColumn] = '.';
                CollectedGold++;
                return "Aranyrög kibányászva.";
        }
    }
}