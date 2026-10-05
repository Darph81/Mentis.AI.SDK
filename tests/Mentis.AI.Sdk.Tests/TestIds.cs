namespace Mentis.AI.Sdk.Tests;

/// <summary>Fixed GUIDs so assertions stay readable (every Manager id is a GUID).</summary>
internal static class TestIds
{
    public static readonly Guid Document1 = Guid.Parse("d0c00000-0000-0000-0000-000000000001");
    public static readonly Guid Document2 = Guid.Parse("d0c00000-0000-0000-0000-000000000002");
    public static readonly Guid Document3 = Guid.Parse("d0c00000-0000-0000-0000-000000000003");
    public static readonly Guid Chunk1 = Guid.Parse("c4c00000-0000-0000-0000-000000000001");
    public static readonly Guid Chunk2 = Guid.Parse("c4c00000-0000-0000-0000-000000000002");
    public static readonly Guid Conversation1 = Guid.Parse("c0c00000-0000-0000-0000-000000000001");
    public static readonly Guid Conversation2 = Guid.Parse("c0c00000-0000-0000-0000-000000000002");
    public static readonly Guid Message1 = Guid.Parse("a5c00000-0000-0000-0000-000000000001");
    public static readonly Guid Tenant1 = Guid.Parse("7e700000-0000-0000-0000-000000000001");
    public static readonly Guid User1 = Guid.Parse("05e00000-0000-0000-0000-000000000001");
}
