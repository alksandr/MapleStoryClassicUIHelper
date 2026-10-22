using WzComparerR2.WzLib;

internal static class ArchiveProbe {
    public static void Run(string root, string category, string? path) {
        var wz = new Wz_Structure();
        try {
            Wz_Node? node = null;
            if (category.EndsWith(".ms")) { wz.LoadMsFile(Path.Combine(root, "Data", "Packs", category)); node = wz.WzNode; }
            else wz.LoadWzFolder(Path.Combine(root, "Data", category), ref node);
            if (path != null) node = node.FindNodeByPath(path.Replace('/', '\\'), true);
            if (node == null) throw new Exception("Node not found");
            Print(node, 0);
        } finally { wz.Clear(); }
    }
    private static void Print(Wz_Node n, int depth) {
        if (depth > 3) return;
        Console.WriteLine($"{new string(' ', depth * 2)}{n.Text} = {n.Value} [{n.Nodes.Count}]");
        foreach (var c in n.Nodes.Cast<Wz_Node>().Take(depth == 0 ? 35 : 10)) Print(c, depth + 1);
    }
}
