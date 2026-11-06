using System;
using System.Collections.Generic;
using System.Linq;

class E : Exception { public string Code; public E(string c) { Code = c; } }

class Interp
{
    static readonly int[] DR = { 0, -1, 0, 1 };   // kelet, észak, nyugat, dél (balra fordulás = index+1)
    static readonly int[] DC = { 1, 0, -1, 0 };
    public const string Arrows = ">^<v";

    string[] init; public int R, C;
    public char[,] G; public bool[,] Exp;
    public int pr, pc, dir, Gold, Count;
    public Dictionary<string, object> V = new Dictionary<string, object>();
    public List<string> Log = new List<string>(), Out = new List<string>();
    public bool Stop, Limit, Done; public string Err;
    string api;

    public Interp(string[] map) { init = map; R = map.Length; C = map[0].Length; Reset(); }

    public void Reset()
    {
        G = new char[R, C]; Exp = new bool[R, C];
        for (int i = 0; i < R; i++)
            for (int j = 0; j < C; j++)
            {
                char ch = init[i][j];
                int d = Arrows.IndexOf(ch);
                if (d >= 0) { pr = i; pc = j; dir = d; ch = '.'; }
                G[i, j] = ch;
            }
        Exp[pr, pc] = true;
        V.Clear(); Log.Clear(); Out.Clear();
        Gold = 0; Count = 0; Stop = Limit = Done = false; Err = null;
    }

    public int Cell(int r, int c) => (r < 0 || c < 0 || r >= R || c >= C || G[r, c] == '#') ? 1 : G[r, c] == '@' ? 2 : 0;
    public int Front() => Cell(pr + DR[dir], pc + DC[dir]);
    public static string F(object o) => o is bool b ? (b ? "true" : "false") : o.ToString();

    object Rd(string t)
    {
        if (t == "true") return true;
        if (t == "false") return false;
        if (char.IsDigit(t[0])) return int.Parse(t);
        if (V.TryGetValue(t, out var v)) return v;
        throw new E("E_UNINITIALIZED");
    }

    object Eval(string[] e)
    {
        if (e.Length == 1) return Rd(e[0]);
        if (e[0] == "isThere")
        {
            api = "isThere"; var p = Rd(e[1]); api += " " + F(p);
            if (!(p is int w) || w < 0 || w > 2) throw new E("E_BAD_ARG");
            int fr = pr + DR[dir], fc = pc + DC[dir];
            if (fr >= 0 && fc >= 0 && fr < R && fc < C) Exp[fr, fc] = true;
            bool res = Front() == w; api += " " + F(res); return res;
        }
        if (e[0] == "not")
        {
            var x = Rd(e[1]); if (!(x is bool bx)) throw new E("E_BAD_OPERAND"); return !bx;
        }
        var a = Rd(e[0]); string op = e[1];
        if (op == "and" || op == "or")
        {
            if (!(a is bool ba)) throw new E("E_BAD_OPERAND");
            var b = Rd(e[2]); if (!(b is bool bb)) throw new E("E_BAD_OPERAND");
            return op == "and" ? ba && bb : ba || bb;
        }
        if (op == "=" || op == "<>")
        {
            var b = Rd(e[2]); if (a.GetType() != b.GetType()) throw new E("E_BAD_OPERAND");
            bool eq = a.Equals(b); return op == "=" ? eq : !eq;
        }
        if (!(a is int ia)) throw new E("E_BAD_OPERAND");
        var b2 = Rd(e[2]); if (!(b2 is int ib)) throw new E("E_BAD_OPERAND");
        switch (op)
        {
            case "+": return unchecked(ia + ib);
            case "-": return unchecked(ia - ib);
            case "*": return unchecked(ia * ib);
            case "<": return ia < ib;
            case "<=": return ia <= ib;
            case ">": return ia > ib;
            default: return ia >= ib;
        }
    }

    // Egy sor végrehajtása + naplózása; if/while esetén a feltétel értékével tér vissza
    bool Do(Cmd c)
    {
        api = null; string err = null; bool cond = false;
        try
        {
            switch (c.T)
            {
                case "step":
                    {
                        api = "step forward"; var p = Rd(c.A[0]); api += " " + F(p);
                        if (!(p is int n) || n < 0) throw new E("E_BAD_ARG");
                        for (int k = 0; k < n; k++)
                        {
                            if (Front() == 1) throw new E("E_BEDROCK_HIT");
                            pr += DR[dir]; pc += DC[dir]; Exp[pr, pc] = true;
                        }
                        break;
                    }
                case "turn":
                    {
                        api = "turn by"; var p = Rd(c.A[0]); api += " " + F(p);
                        if (!(p is int n) || n < 0) throw new E("E_BAD_ARG");
                        dir = (dir + n % 4) % 4; break;
                    }
                case "mine":
                    api = "mine here";
                    if (Cell(pr, pc) != 2) throw new E("E_NO_GOLD");
                    G[pr, pc] = '.'; Gold++; break;
                case "print":
                    { api = "print"; var p = Rd(c.A[0]); api += " " + F(p); Out.Add(F(p)); break; }
                case "set":
                    {
                        var v = Eval(c.A.Skip(1).ToArray());
                        if (V.TryGetValue(c.A[0], out var old) && old.GetType() != v.GetType()) throw new E("E_TYPE_MISMATCH");
                        V[c.A[0]] = v; break;
                    }
                default:
                    {
                        var p = Rd(c.A[0]);
                        if (!(p is bool b)) throw new E("E_NON_BOOL_CONDITION");
                        cond = b; break;
                    }
            }
        }
        catch (E e) { err = e.Code; }
        Log.Add(c.Line + (api != null ? " " + api : "") + (err != null ? " " + err : ""));
        if (err != null) { Stop = true; Err = err; }
        return cond;
    }

    bool Begin()
    {
        if (Count >= 1000)
        {
            Log.Add("INSTRUCTION_COUNT_LIMIT_EXCEEDED"); Stop = Limit = true; Err = "INSTRUCTION_COUNT_LIMIT_EXCEEDED"; return false;
        }
        Count++; return true;
    }

    public void Finish() { if (!Limit) Log.Add("EOR"); Done = true; }

    // Végrehajtás generátorként: minden utasítás VÉGREHAJTÁSA ELŐTT visszaadja a sor számát (nyomkövetés).
    public IEnumerable<int> Run(List<Cmd> prog) { L.Num(prog); return Exec(prog); }

    IEnumerable<int> Exec(List<Cmd> list)
    {
        foreach (var c in list)
        {
            foreach (var x in ExecCmd(c)) yield return x;
            if (Stop) yield break;
        }
    }

    IEnumerable<int> ExecCmd(Cmd c)
    {
        while (true)
        {
            yield return c.Line;
            if (!Begin()) yield break;
            bool cond = Do(c);
            if (Stop) yield break;
            if (c.T == "while")
            {
                if (!cond) yield break;
                foreach (var x in Exec(c.Body)) yield return x;
                if (Stop) yield break;
                continue;
            }
            if (c.T == "if")
            {
                if (cond) { foreach (var x in Exec(c.Body)) yield return x; }
                else if (c.Else != null)
                {
                    yield return c.ElseLine;
                    if (!Begin()) yield break;
                    Log.Add(c.ElseLine.ToString());
                    foreach (var x in Exec(c.Else)) yield return x;
                }
            }
            yield break;
        }
    }

    // Kézi vezérlés (nincs naplózás, ütközésnél hiba nélkül megáll). Üzenettel tér vissza.
    public string Manual(string cmd, int n)
    {
        switch (cmd)
        {
            case "f":
                for (int k = 0; k < n; k++)
                {
                    if (Front() == 1) return "Falnak ütköztél, a robot megállt.";
                    pr += DR[dir]; pc += DC[dir]; Exp[pr, pc] = true;
                }
                return "Lépés kész.";
            case "l": dir = (dir + n % 4) % 4; return "Fordulás kész.";
            default:
                if (Cell(pr, pc) != 2) return "Itt nincs arany.";
                G[pr, pc] = '.'; Gold++; return "Aranyrög kibányászva.";
        }
    }
}
