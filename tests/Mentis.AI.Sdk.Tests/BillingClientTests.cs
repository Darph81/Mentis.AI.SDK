using Grpc.Core;

using NSubstitute;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk.Tests;

public class BillingClientTests
{
    private Proto.BillingService.BillingServiceClient _grpc = null!;
    private BillingClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _grpc = Substitute.For<Proto.BillingService.BillingServiceClient>();
        _client = new BillingClient(_grpc);
    }

    [Test]
    public async Task GetUsageAsync_WithoutArguments_LeavesYearAndMonthUnset()
    {
        Proto.GetTenantUsageRequest? sent = null;
        _grpc.GetTenantUsageAsync(Arg.Do<Proto.GetTenantUsageRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.GetTenantUsageResponse
            {
                Usage = new Proto.TenantUsage { TenantId = TestIds.Tenant1.ToString(), Year = 2026, Month = 9, TotalTokens = 1234 },
            }));

        TenantUsage usage = await _client.GetUsageAsync();

        sent!.HasYear.ShouldBeFalse();
        sent.HasMonth.ShouldBeFalse();
        sent.HasTenantId.ShouldBeFalse();
        usage.TotalTokens.ShouldBe(1234);
        usage.MonthlyTokenLimit.ShouldBeNull();
    }

    [Test]
    public async Task GetUsageAsync_WithYearAndMonth_SendsThem()
    {
        Proto.GetTenantUsageRequest? sent = null;
        _grpc.GetTenantUsageAsync(Arg.Do<Proto.GetTenantUsageRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.GetTenantUsageResponse
            {
                Usage = new Proto.TenantUsage { TenantId = TestIds.Tenant1.ToString(), Year = 2026, Month = 3, MonthlyTokenLimit = 5000 },
            }));

        TenantUsage usage = await _client.GetUsageAsync(2026, 3);

        sent!.Year.ShouldBe(2026);
        sent.Month.ShouldBe(3);
        usage.MonthlyTokenLimit.ShouldBe(5000);
    }

    [Test]
    public async Task GetUsageAsync_InvalidMonth_Throws()
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => _client.GetUsageAsync(2026, 13));
    }

    [Test]
    public async Task GetUsageHistoryAsync_MapsAllMonths()
    {
        _grpc.ListTenantUsageHistoryAsync(Arg.Any<Proto.ListTenantUsageHistoryRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.ListTenantUsageHistoryResponse
            {
                History =
                {
                    new Proto.TenantUsage { Year = 2026, Month = 9 },
                    new Proto.TenantUsage { Year = 2026, Month = 8 },
                },
            }));

        IReadOnlyList<TenantUsage> history = await _client.GetUsageHistoryAsync();

        history.Select(u => u.Month).ShouldBe([9, 8]);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task GetUsageAsync_NonPositiveYear_ThrowsBeforeCallingServer(int year)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => _client.GetUsageAsync(year, 5));

        _grpc.ReceivedCalls().ShouldBeEmpty();
    }

    [TestCase(0)]
    [TestCase(13)]
    public async Task GetUsageAsync_MonthOutOfRange_ThrowsBeforeCallingServer(int month)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => _client.GetUsageAsync(2026, month));

        _grpc.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task GetUsageHistoryAsync_ServerError_ThrowsMentisException()
    {
        _grpc.ListTenantUsageHistoryAsync(Arg.Any<Proto.ListTenantUsageHistoryRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Failure<Proto.ListTenantUsageHistoryResponse>(StatusCode.Unauthenticated, "Auth.Invalid: bad token"));

        MentisException ex = await Should.ThrowAsync<MentisException>(() => _client.GetUsageHistoryAsync());

        ex.StatusCode.ShouldBe(MentisStatusCode.Unauthenticated);
        ex.ErrorCode.ShouldBe("Auth.Invalid");
    }
}
