using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

class Program
{
    static string inPath, outPath;
    static string[] mapLines;
    static List<Cmd> prog;
    static Interp I;
    static IEnumerator<int> en;   // aktív futás (nyomkövetés) – null, ha nincs
    static int next;              // a következőként végrehajtandó sor
    static int delay = 250;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        bool quiet = args.Length > 0 && args[0] == "-q";
        var a = quiet ? args.Skip(1).ToArray() : args;
        if (a.Length < 2) { Console.WriteLine("Használat: robot [-q] input.txt output.txt"); return 1; }
        inPath = a[0]; outPath = a[1];
        Load();
        if (quiet)
        {
            I.Reset();
            foreach (var _ in I.Run(prog)) { }
            I.Finish(); WriteLog();
            return 0;
        }
        Repl();
        return 0;
    }

    static void Load()
    {
        var lines = File.ReadAllLines(inPath).Select(s => s.TrimEnd('\r')).ToList();
        var d = lines[0].Trim().Split(' ');
        int r = int.Parse(d[0]);
        mapLines = lines.Skip(1).Take(r).Select(s => s.TrimEnd()).ToArray();
        prog = L.Parse(lines.Skip(1 + r));
        I = new Interp(mapLines); L.Num(prog);
    }

    static void WriteLog() { File.WriteAllLines(outPath, I.Log); }

    // ---------- futtatás ----------
    static void Start() { I.Reset(); L.Num(prog); en = I.Run(prog).GetEnumerator(); next = 0; WriteLog(); }

    static bool Adv()
    {
        if (en == null) return false;
        if (en.MoveNext()) next = en.Current;
        else { I.Finish(); en = null; next = 0; }
        WriteLog();
        return en != null;
    }

    static void RunSlow()
    {
        Start();
        while (Adv())
        {
            Redraw("Futtatás… (bármely billentyű: megszakítás)");
            Thread.Sleep(delay);
            try { if (Console.KeyAvailable) { Console.ReadKey(true); en = null; WriteLog(); Console.WriteLine("Megszakítva."); return; } } catch { }
        }
        Redraw("A futás befejeződött.");
    }

    // ---------- megjelenítés ----------
    static void Redraw(string status)
    {
        try { Console.Clear(); } catch { }
        Show(status);
    }

    static void Show(string status)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== SZIMULÁTOR  (?=feltérképezetlen  .=üres  #=fal  @=arany  > ^ < v = robot) ===");
        for (int i = 0; i < I.R; i++)
        {
            for (int j = 0; j < I.C; j++)
            {
                char ch = (i == I.pr && j == I.pc) ? Interp.Arrows[I.dir] : I.Exp[i, j] ? I.G[i, j] : '?';
                sb.Append(ch).Append(' ');
            }
            sb.AppendLine();
        }
        string[] nm = { "üres kamra", "fal", "aranyrög" };
        sb.AppendLine($"Szenzor (a robot előtt): {nm[I.Front()]}   |   Kibányászott arany: {I.Gold}");
        sb.AppendLine("Változók: " + (I.V.Count == 0 ? "-" : string.Join(", ", I.V.Select(kv => kv.Key + "=" + Interp.F(kv.Value)))));
        sb.AppendLine("Konzol: " + (I.Out.Count == 0 ? "-" : string.Join(" | ", I.Out.Skip(Math.Max(0, I.Out.Count - 15)))));
        if (I.Err != null) sb.AppendLine("HIBA: " + I.Err);
        sb.AppendLine("Állapot: " + status);
        Console.Write(sb.ToString());
    }

    static void ShowText()
    {
        L.Num(prog);
        var t = L.Text(prog); Console.WriteLine("=== SZÖVEGES FORRÁSKÓD ===");
        for (int i = 0; i < t.Count; i++) Console.WriteLine((i + 1 == next && en != null ? "=> " : "   ") + (i + 1).ToString().PadLeft(3) + "  " + t[i]);
        if (t.Count == 0) Console.WriteLine("   (üres program)");
    }

    static void ShowBlocks()
    {
        var o = new List<string>(); L.Blocks(prog, "", 0, o);
        Console.WriteLine("=== BLOKK-NÉZET (a címkék a szerkesztő pozíciói) ===");
        if (o.Count == 0) Console.WriteLine("(üres program – használd: add <blokk>)");
        o.ForEach(Console.WriteLine);
    }

    static void Help()
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

    // ---------- szerkesztés ----------
    static bool Locate(string path, out List<Cmd> list, out int idx, out int depth)
    {
        list = prog; idx = 0; depth = 0;
        var tk = path.Split('.'); Cmd c = null; bool entered = true;
        for (int k = 0; k < tk.Length; k++)
        {
            if (tk[k] == "e")
            {
                if (c == null || c.Else == null || k == tk.Length - 1) return false;
                list = c.Else; entered = true; continue;
            }
            if (!entered) { if (c.T != "if" && c.T != "while") return false; list = c.Body; }
            idx = int.Parse(tk[k]) - 1;
            if (idx < 0) return false;
            if (k < tk.Length - 1)
            {
                if (idx >= list.Count) return false;
                c = list[idx]; entered = false; depth++;
            }
        }
        return idx <= list.Count;
    }

    static bool Edit(string cmd, string rest)
    {
        var pm = Regex.Match(rest, @"^(\d+(?:\.(?:\d+|e))*)(?:\s+(.*))?$");
        if (cmd == "add") { return Insert(prog, prog.Count, 0, rest); }
        if (!pm.Success) { Console.WriteLine("Hibás cím. Nézd meg a 'blocks' nézet címkéit."); return false; }
        string path = pm.Groups[1].Value, blk = pm.Groups[2].Value;
        if (cmd == "into")
        {
            bool isElse = path.EndsWith(".e");
            string p = isElse ? path.Substring(0, path.Length - 2) : path;
            if (!Locate(p, out var l, out var ix, out var dp) || ix >= l.Count) { Console.WriteLine("Nincs ilyen blokk."); return false; }
            var c = l[ix];
            if (c.T != "if" && c.T != "while") { Console.WriteLine("Csak vezérlőblokk törzsébe lehet beszúrni."); return false; }
            if (isElse && c.Else == null) { Console.WriteLine("Ennek az if-nek nincs else ága."); return false; }
            var tgt = isElse ? c.Else : c.Body;
            return Insert(tgt, tgt.Count, dp + 1, blk);
        }
        if (!Locate(path, out var list, out var idx, out var depth)) { Console.WriteLine("Nincs ilyen pozíció."); return false; }
        switch (cmd)
        {
            case "ins": return Insert(list, idx, depth, blk);
            case "after":
                if (idx >= list.Count) { Console.WriteLine("Nincs ilyen blokk."); return false; }
                return Insert(list, idx + 1, depth, blk);
            case "del":
                if (idx >= list.Count) { Console.WriteLine("Nincs ilyen blokk."); return false; }
                list.RemoveAt(idx); return true;
            case "edit":
                {
                    if (idx >= list.Count) { Console.WriteLine("Nincs ilyen blokk."); return false; }
                    var t = Tok(blk); var err = L.Validate(t);
                    if (err != null) { Console.WriteLine(err); return false; }
                    var nc = L.From(t); var old = list[idx];
                    if (nc.T != old.T) { Console.WriteLine("Az edit csak azonos típusú blokkra működik (töröld és szúrd be újra)."); return false; }
                    old.A = nc.A; return true;
                }
        }
        return false;
    }

    static string[] Tok(string s) => (s ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // depth = a beszúrási hely körüli vezérlőblokkok száma; vezérlőblokk csak ha depth <= 2 (max. 3 szint)
    static bool Insert(List<Cmd> list, int idx, int depth, string blk)
    {
        var t = Tok(blk); var err = L.Validate(t);
        if (err != null) { Console.WriteLine(err); return false; }
        var c = L.From(t);
        if ((c.T == "if" || c.T == "while") && depth >= 3) { Console.WriteLine("Legfeljebb 3 szint mélyen ágyazhatók egymásba a vezérlőblokkok."); return false; }
        list.Insert(idx, c); return true;
    }

    // ---------- fő ciklus ----------
    static void Repl()
    {
        Console.WriteLine("MineScript IDE – 'help' a parancsokért.");
        Redraw("Alaphelyzet."); ShowText();
        while (true)
        {
            Console.Write("\n> ");
            var ln = Console.ReadLine(); if (ln == null) return;
            var p = ln.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            string cmd = p[0].ToLower(), rest = p.Length > 1 ? p[1] : "";
            try
            {
                switch (cmd)
                {
                    case "help": Help(); break;
                    case "exit": return;
                    case "text": ShowText(); break;
                    case "blocks": ShowBlocks(); break;
                    case "map": Show(en != null ? "Nyomkövetés folyamatban." : "Kész / alaphelyzet."); break;
                    case "log": Console.WriteLine(File.Exists(outPath) ? File.ReadAllText(outPath) : "(még nincs napló)"); break;
                    case "speed": delay = Math.Max(0, int.Parse(rest)); Console.WriteLine("Késleltetés: " + delay + " ms"); break;
                    case "save":
                        File.WriteAllLines(inPath, new[] { I.R + " " + I.C }.Concat(mapLines).Concat(L.Text(prog)));
                        Console.WriteLine("Mentve: " + inPath); break;
                    case "add":
                    case "ins":
                    case "after":
                    case "into":
                    case "edit":
                    case "del":
                        if (Edit(cmd, rest)) { en = null; I.Reset(); L.Num(prog); WriteLog(); ShowBlocks(); Console.WriteLine(); ShowText(); }
                        break;
                    case "run": RunSlow(); break;
                    case "debug": Start(); Adv(); Show("Nyomkövetés: a következő sor a ⇒ jelölt."); ShowText(); break;
                    case "n":
                        {
                            if (en == null) { Console.WriteLine("Nincs aktív nyomkövetés (debug)."); break; }
                            int cnt = rest == "" ? 1 : int.Parse(rest);
                            for (int k = 0; k < cnt && Adv(); k++) { }
                            Show(en != null ? "Nyomkövetés: a következő sor a ⇒ jelölt." : "A program véget ért."); ShowText(); break;
                        }
                    case "stop": en = null; Console.WriteLine("Futás megszakítva (a robot állapota megmaradt)."); break;
                    case "f":
                    case "l":
                    case "m":
                        {
                            if (en != null) { en = null; Console.WriteLine("A programfutás megszakadt, kézi vezérlés."); }
                            string msg = I.Manual(cmd, rest == "" ? 1 : int.Parse(rest));
                            Show("Kézi vezérlés: " + msg); break;
                        }
                    default: Console.WriteLine("Ismeretlen parancs. 'help'"); break;
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException) { Console.WriteLine("Hibás szám."); }
        }
    }
}
