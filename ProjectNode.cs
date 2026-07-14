using System.Collections.Generic;

namespace ProjectStructureSummary;

public enum NodeKind
{
    Solution,
    Project,
    Folder,
    File
}

public sealed class ProjectNode
{
    public NodeKind Kind { get; }
    public string Name { get; }
    public string FullPath { get; }

    public ProjectNode? Parent { get; private set; }
    public List<ProjectNode> Children { get; } = new();

    public ProjectNode(NodeKind kind, string name, string fullPath)
    {
        Kind = kind;
        Name = name;
        FullPath = fullPath;
    }

    public void AddChild(ProjectNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public ProjectNode? FindAncestor(NodeKind kind)
    {
        var cur = Parent;
        while (cur is not null)
        {
            if (cur.Kind == kind)
                return cur;

            cur = cur.Parent;
        }
        return null;
    }

    public IEnumerable<ProjectNode> DescendantsAndSelf()
    {
        yield return this;

        foreach (var child in Children)
        {
            foreach (var nested in child.DescendantsAndSelf())
                yield return nested;
        }
    }
}
