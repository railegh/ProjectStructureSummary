using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectStructureSummary;

public static class ProjectScanner
{
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".vs", ".git", ".idea", ".vscode", "packages"
    };

    private static readonly Regex SlnProjectLineRx = new(
        "^Project\\(\"\\{[^\\}]+\\}\"\\)\\s*=\\s*\"(?<name>[^\"]+)\",\\s*\"(?<path>[^\"]+)\"\\s*,\\s*\"\\{[^\\}]+\\}\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<ProjectNode> BuildTree(string rootFolder)
    {
        var result = new List<ProjectNode>();
        var slnFiles = FindSolutionFiles(rootFolder);

        if (slnFiles.Count == 0)
        {
            // fallback: no sln, build a virtual solution from csproj
            var solutionName = Path.GetFileName(
                rootFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            var solutionNode = new ProjectNode(NodeKind.Solution, solutionName, rootFolder);

            var projects = SafeEnumerateFilesRecursive(rootFolder, "*.csproj")
                .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var projectPath in projects)
            {
                var projectNode = BuildProjectNode(projectPath);
                solutionNode.AddChild(projectNode);
            }

            result.Add(solutionNode);
            return result;
        }

        foreach (var slnPath in slnFiles)
        {
            var slnName = Path.GetFileNameWithoutExtension(slnPath);
            var slnDir = Path.GetDirectoryName(slnPath)!;

            var solutionNode = new ProjectNode(NodeKind.Solution, slnName, slnPath);

            var relProjects = ParseProjectPathsFromSolution(slnPath).ToList();

            // if the sln is empty/non-standard — soft fallback to csproj nearby
            if (relProjects.Count == 0)
            {
                relProjects = SafeEnumerateFilesRecursive(slnDir, "*.csproj")
                    .Select(p => Path.GetRelativePath(slnDir, p))
                    .ToList();
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rel in relProjects)
            {
                var fullProjectPath = Path.GetFullPath(Path.Combine(slnDir, rel));

                if (!seen.Add(fullProjectPath))
                    continue;

                ProjectNode projectNode;
                if (File.Exists(fullProjectPath))
                {
                    projectNode = BuildProjectNode(fullProjectPath);
                }
                else
                {
                    var projectName = Path.GetFileNameWithoutExtension(rel);
                    projectNode = new ProjectNode(NodeKind.Project, projectName, fullProjectPath);
                }

                solutionNode.AddChild(projectNode);
            }

            result.Add(solutionNode);
        }

        return result;
    }

    public static string BuildReportForSelection(ProjectNode selected, ISet<ProjectNode>? includedNodes = null) {
        bool IsIncluded(ProjectNode n) => includedNodes is null || includedNodes.Contains(n);

        var solution = selected.Kind == NodeKind.Solution
            ? selected
            : selected.FindAncestor(NodeKind.Solution)
              ?? throw new InvalidOperationException("Selected node is not attached to a solution.");

        var projectForReport = selected.Kind switch {
            NodeKind.Solution => null,
            NodeKind.Project => selected,
            _ => selected.FindAncestor(NodeKind.Project)
        };

        var filesRoot = projectForReport ?? solution;

        var sb = new StringBuilder();

        sb.AppendLine("### Project Structure");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine();

        AppendSolutionStructure(sb, solution, projectForReport, 0, IsIncluded);

        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine();

        var files = filesRoot
            .DescendantsAndSelf()
            .Where(x => x.Kind == NodeKind.File)
            .Where(IsIncluded)
            .OrderBy(x => GetDisplayPath(solution, x), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0) {
            sb.AppendLine("_No .cs files selected in current scope._");
            return sb.ToString().TrimEnd();
        }

        foreach (var file in files) {
            var displayPath = GetDisplayPath(solution, file);

            sb.AppendLine($"### {displayPath}");
            sb.AppendLine();
            sb.AppendLine("```cs");
            sb.AppendLine();

            var content = TryReadFile(file.FullPath);
            sb.AppendLine(NormalizeLineEndings(content));

            sb.AppendLine("```");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }


    private static void AppendSolutionStructure(
        StringBuilder sb,
        ProjectNode solution,
        ProjectNode? onlyProject,
        int level,
        Func<ProjectNode, bool> isIncluded) {
        sb.AppendLine($"{Indent(level)}[solution] {solution.Name} {{");

        IEnumerable<ProjectNode> projects = onlyProject is null
            ? solution.Children.Where(c => c.Kind == NodeKind.Project)
            : new[] { onlyProject };

        foreach (var project in projects)
            AppendCompositeNode(sb, project, level + 1, isIncluded);

        sb.AppendLine($"{Indent(level)}}}");
    }

    private static bool AppendCompositeNode(
        StringBuilder sb,
        ProjectNode node,
        int level,
        Func<ProjectNode, bool> isIncluded) {
        if (!HasIncludedContent(node, isIncluded))
            return false;

        switch (node.Kind) {
            case NodeKind.Project:
                sb.AppendLine($"{Indent(level)}[project] {node.Name} {{");

                foreach (var file in node.Children.Where(c => c.Kind == NodeKind.File && isIncluded(c)))
                    sb.AppendLine($"{Indent(level + 1)}{file.Name}");

                foreach (var folder in node.Children.Where(c => c.Kind == NodeKind.Folder))
                    AppendCompositeNode(sb, folder, level + 1, isIncluded);

                sb.AppendLine($"{Indent(level)}}}");
                return true;

            case NodeKind.Folder:
                sb.AppendLine($"{Indent(level)}[folder] {node.Name} {{");

                foreach (var file in node.Children.Where(c => c.Kind == NodeKind.File && isIncluded(c)))
                    sb.AppendLine($"{Indent(level + 1)}{file.Name}");

                foreach (var folder in node.Children.Where(c => c.Kind == NodeKind.Folder))
                    AppendCompositeNode(sb, folder, level + 1, isIncluded);

                sb.AppendLine($"{Indent(level)}}}");
                return true;

            case NodeKind.File:
                if (!isIncluded(node))
                    return false;

                sb.AppendLine($"{Indent(level)}{node.Name}");
                return true;

            default:
                return false;
        }
    }

    private static bool HasIncludedContent(ProjectNode node, Func<ProjectNode, bool> isIncluded) {
        if (node.Kind == NodeKind.File)
            return isIncluded(node);

        foreach (var child in node.Children) {
            if (HasIncludedContent(child, isIncluded))
                return true;
        }

        return false;
    }

    private static ProjectNode BuildProjectNode(string projectFilePath)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
        var projectDir = Path.GetDirectoryName(projectFilePath)!;

        var projectNode = new ProjectNode(NodeKind.Project, projectName, projectFilePath);
        AppendDirectoryChildren(projectNode, projectDir);

        return projectNode;
    }

    private static void AppendDirectoryChildren(ProjectNode parent, string dir)
    {
        var files = SafeGetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in files)
        {
            var fileNode = new ProjectNode(NodeKind.File, Path.GetFileName(file), file);
            parent.AddChild(fileNode);
        }

        var subDirs = SafeGetDirectories(dir)
            .Where(d => !IsExcludedDirName(Path.GetFileName(d)))
            .Where(d => !IsReparsePoint(d))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var sub in subDirs)
        {
            var folderNode = new ProjectNode(NodeKind.Folder, Path.GetFileName(sub), sub);
            AppendDirectoryChildren(folderNode, sub);

            // show only if there is real content (files/subfolders)
            if (folderNode.Children.Count > 0)
                parent.AddChild(folderNode);
        }
    }

    private static List<string> FindSolutionFiles(string rootFolder)
    {
        var top = SafeGetFiles(rootFolder, "*.sln", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (top.Count > 0)
            return top;

        var all = SafeEnumerateFilesRecursive(rootFolder, "*.sln")
            .OrderBy(p => GetDepthRelativeToRoot(p, rootFolder))
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return all;
    }

    private static int GetDepthRelativeToRoot(string filePath, string root)
    {
        var rel = Path.GetRelativePath(root, filePath);
        int depth = 0;

        foreach (var ch in rel)
        {
            if (ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar)
                depth++;
        }

        return depth;
    }

    private static IEnumerable<string> ParseProjectPathsFromSolution(string slnPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(slnPath))
        {
            var m = SlnProjectLineRx.Match(line);
            if (!m.Success)
                continue;

            var projectPath = m.Groups["path"].Value.Trim();

            if (!projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                continue;

            projectPath = projectPath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            if (seen.Add(projectPath))
                yield return projectPath;
        }
    }

    private static string TryReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return $"// failed to read file: {ex.Message}";
        }
    }

    private static string GetDisplayPath(ProjectNode solution, ProjectNode fileNode)
    {
        var baseDir = ResolveSolutionBaseDir(solution);
        var rel = Path.GetRelativePath(baseDir, fileNode.FullPath);
        return rel.Replace('/', '\\');
    }

    private static string ResolveSolutionBaseDir(ProjectNode solution)
    {
        // if real .sln
        if (solution.FullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) && File.Exists(solution.FullPath))
            return Path.GetDirectoryName(solution.FullPath)!;

        // fallback virtual solution
        if (Directory.Exists(solution.FullPath))
            return solution.FullPath;

        return Path.GetDirectoryName(solution.FullPath) ?? Environment.CurrentDirectory;
    }

    private static IEnumerable<string> SafeEnumerateFilesRecursive(string root, string pattern)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            IEnumerable<string> files = Array.Empty<string>();
            try { files = Directory.GetFiles(current, pattern, SearchOption.TopDirectoryOnly); } catch { }

            foreach (var file in files)
                yield return file;

            IEnumerable<string> dirs = Array.Empty<string>();
            try { dirs = Directory.GetDirectories(current); } catch { }

            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                if (IsExcludedDirName(name))
                    continue;
                if (IsReparsePoint(dir))
                    continue;

                stack.Push(dir);
            }
        }
    }

    private static IEnumerable<string> SafeGetDirectories(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeGetFiles(string path, string pattern, SearchOption option)
    {
        try { return Directory.GetFiles(path, pattern, option); }
        catch { return Array.Empty<string>(); }
    }

    private static bool IsExcludedDirName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && ExcludedDirs.Contains(name);

    private static bool IsReparsePoint(string dir)
    {
        try
        {
            var attr = File.GetAttributes(dir);
            return attr.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true;
        }
    }

    private static string NormalizeLineEndings(string s) =>
        s.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string Indent(int level) => new(' ', level * 4);
}
