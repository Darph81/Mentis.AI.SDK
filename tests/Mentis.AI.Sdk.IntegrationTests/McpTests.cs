using Grpc.Core;

namespace Mentis.AI.Sdk.IntegrationTests;

/// <summary>
/// MCP wiring against a real Manager. Only the rejection paths are tested: they are deterministic, need no MCP
/// server and cost no tokens (the Manager validates the server before any LLM call).
/// </summary>
public class McpTests : IntegrationTest
{
    [Test]
    public async Task SendMessage_WithHostNotOnAllowList_IsPermissionDenied()
    {
        Conversation conversation = await StartConversationAsync(Client.Conversations);

        MentisException ex = await Should.ThrowAsync<MentisException>(
            () => Client.Conversations.SendMessageAsync(
                conversation.Id,
                "Create a ticket.",
                mcpServer: new McpServer
                {
                    Url = new Uri("https://not-allow-listed.invalid/mcp"),
                    AuthHeader = "Bearer never-used",
                    AllowedToolNames = ["create_ticket"],
                },
                cancellationToken: Timeout));

        ex.StatusCode.ShouldBe(MentisStatusCode.PermissionDenied);
        ex.ErrorCode.ShouldBe("Mcp.ServerNotAllowed");
        ex.Message.ShouldNotContain("never-used");
    }
}
