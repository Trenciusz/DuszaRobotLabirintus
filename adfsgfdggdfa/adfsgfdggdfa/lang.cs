using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

// Egy program-blokk (utasítás). Az "if" Else listája null, ha nincs else ág.
class Cmd
{
    public string T;                       // step, turn, mine, print, set, if, while
    public string[] A = new string[0];     // mezők (set: A[0]=ident, A[1..]=kifejezés tokenek)
    public List<Cmd> Body = new List<Cmd>();
    public List<Cmd> Else;
    public int Line, ElseLine;

    public string Head()
    {
        switch (T)
        {
            case "step": return "step forward " + A[0];
            case "turn": return "turn by " + A[0];
            case "mine": return "mine here";
            case "print": return "print " + A[0];
            case "set": return "set " + A[0] + " to " + string.Join(" ", A.Skip(1));
            case "if": return "if " + A[0];
            default: return "while " + A[0];
        }
    }
    // Blokk-nézet: a szerkeszthető mezők [szögletes zárójelben]
    public string Blk()
    {
        switch (T)
        {
            case "step": return "step forward [" + A[0] + "]";
            case "turn": return "turn by [" + A[0] + "]";
            case "mine": return "mine here";
            case "print": return "print [" + A[0] + "]";
            case "set": return "set [" + A[0] + "] to " + string.Join(" ", A.Skip(1).Select(x => L.Ops.Contains(x) ? x : "[" + x + "]"));
            case "if": return "if [" + A[0] + "]" + (Else != null ? "  (if-else)" : "");
            default: return "while [" + A[0] + "]";
        }
    }
}

static class L
{
    public static readonly HashSet<string> Kws = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "and","by","else","false","forward","here","if","isThere","mine","not","or","print","set","step","to","true","turn","while" };
    public static readonly HashSet<string> BinOps = new HashSet<string> { "+", "-", "*", "=", "<>", "<", "<=", ">", ">=", "and", "or" };
    public static readonly HashSet<string> Ops = new HashSet<string>(BinOps) { "isThere", "not" };

    public static bool Ident(string s) => Regex.IsMatch(s, @"^[A-Za-z][A-Za-z0-9]*$") && !Kws.Contains(s);
    public static bool Simple(string s) =>
        s == "true" || s == "false" || (Regex.IsMatch(s, @"^[0-9]{1,3}$") && int.Parse(s) <= 100) || Ident(s);

    // null = érvényes blokk, különben hibaüzenet. ("ifelse X" = if-else blokk)
    public static string Validate(string[] t)
    {
        if (t.Length == 0) return "Üres blokk.";
        bool ok;
        string usage;
        switch (t[0])
        {
            case "step": ok = t.Length == 3 && t[1] == "forward" && Simple(t[2]); usage = "step forward <érték>"; break;
            case "turn": ok = t.Length == 3 && t[1] == "by" && Simple(t[2]); usage = "turn by <érték>"; break;
            case "mine": ok = t.Length == 2 && t[1] == "here"; usage = "mine here"; break;
            case "print": ok = t.Length == 2 && Simple(t[1]); usage = "print <érték>"; break;
            case "if": case "ifelse": case "while": ok = t.Length == 2 && Simple(t[1]); usage = t[0] + " <érték>"; break;
            case "set":
                usage = "set <változó> to <érték | isThere x | not x | x művelet y>";
                if (t.Length < 4 || t[2] != "to") { ok = false; break; }
                if (!Ident(t[1])) return "Érvénytelen változónév (kulcsszó vagy hibás azonosító): " + t[1];
                var e = t.Skip(3).ToArray();
                ok = (e.Length == 1 && Simple(e[0])) ||
                     (e.Length == 2 && (e[0] == "isThere" || e[0] == "not") && Simple(e[1])) ||
                     (e.Length == 3 && Simple(e[0]) && BinOps.Contains(e[1]) && Simple(e[2]));
                break;
            default: return "Ismeretlen blokk: " + t[0];
        }
        return ok ? null : "Érvénytelen blokk / mező. Forma: " + usage + " (azonosító: betű+betű/szám, nem kulcsszó; szám: 0..100)";
    }

    public static Cmd From(string[] t)
    {
        var c = new Cmd();
        switch (t[0])
        {
            case "step": case "turn": c.T = t[0]; c.A = new[] { t[2] }; break;
            case "mine": c.T = "mine"; break;
            case "print": case "if": case "while": c.T = t[0]; c.A = new[] { t[1] }; break;
            case "ifelse": c.T = "if"; c.A = new[] { t[1] }; c.Else = new List<Cmd>(); break;
            case "set": c.T = "set"; c.A = new[] { t[1] }.Concat(t.Skip(3)).ToArray(); break;
        }
        return c;
    }

    public static List<Cmd> Parse(IEnumerable<string> src)
    {
        var ls = new List<(int ind, string[] t)>();
        foreach (var raw in src)
        {
            var s = raw.TrimEnd();
            if (s.Length == 0) continue;
            int ind = s.Length - s.TrimStart(' ').Length;
            ls.Add((ind, s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        }
        int i = 0;
        return ParseBlock(ls, ref i, 0);
    }

    static List<Cmd> ParseBlock(List<(int ind, string[] t)> ls, ref int i, int ind)
    {
        var r = new List<Cmd>();
        while (i < ls.Count && ls[i].ind == ind && ls[i].t[0] != "else")
        {
            var c = From(ls[i].t); i++;
            if (c.T == "if" || c.T == "while")
            {
                if (i < ls.Count && ls[i].ind > ind) c.Body = ParseBlock(ls, ref i, ls[i].ind);
                if (c.T == "if" && i < ls.Count && ls[i].ind == ind && ls[i].t[0] == "else")
                {
                    i++; c.Else = new List<Cmd>();
                    if (i < ls.Count && ls[i].ind > ind) c.Else = ParseBlock(ls, ref i, ls[i].ind);
                }
            }
            r.Add(c);
        }
        return r;
    }

    public static int Num(List<Cmd> p, int n = 0)
    {
        foreach (var c in p)
        {
            c.Line = ++n; n = Num(c.Body, n);
            if (c.Else != null) { c.ElseLine = ++n; n = Num(c.Else, n); }
        }
        return n;
    }

    // Szöveges forráskód (2 szóköz behúzás)
    public static List<string> Text(List<Cmd> p)
    {
        var o = new List<string>(); Rend(p, 0, o); return o;
    }
    static void Rend(List<Cmd> p, int ind, List<string> o)
    {
        var sp = new string(' ', ind);
        foreach (var c in p)
        {
            o.Add(sp + c.Head()); Rend(c.Body, ind + 2, o);
            if (c.Else != null) { o.Add(sp + "else"); Rend(c.Else, ind + 2, o); }
        }
    }

    // Blokk-nézet útvonal-címkékkel (ezekkel lehet a szerkesztőparancsokban hivatkozni a pozíciókra)
    public static void Blocks(List<Cmd> p, string pre, int ind, List<string> o)
    {
        var sp = new string(' ', ind);
        for (int i = 0; i < p.Count; i++)
        {
            var c = p[i]; string path = pre + (i + 1);
            o.Add(sp + "┌ " + path.PadRight(6) + c.Blk());
            if (c.T == "if" || c.T == "while")
            {
                if (c.Body.Count == 0) o.Add(sp + "│   (üres blokk: " + path + ")");
                Blocks(c.Body, path + ".", ind + 4, o);
                if (c.Else != null)
                {
                    o.Add(sp + "│  else:");
                    if (c.Else.Count == 0) o.Add(sp + "│   (üres blokk: " + path + ".e)");
                    Blocks(c.Else, path + ".e.", ind + 4, o);
                }
            }
        }
    }
}
