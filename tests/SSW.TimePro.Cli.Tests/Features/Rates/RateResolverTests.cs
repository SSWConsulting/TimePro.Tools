using FluentAssertions;
using SSW.TimePro.Cli.Features.Rates;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Rates;

public class RateResolverTests
{
    // ── Recommend ────────────────────────────────────────────────

    [Fact]
    public void Recommend_PrefersPreviousRate_OverEmployeeDefault()
    {
        var init = new ClientRateInit
        {
            PreviousRate = 325, PreviousPrepaidRate = 300,
            DefaultRate = 200, DefaultPrepaidRate = 185
        };

        var rec = RateResolver.Recommend(init);

        rec.Source.Should().Be(RateSource.Previous);
        rec.Rate.Should().Be(325);
        rec.PrepaidRate.Should().Be(300);
    }

    [Fact]
    public void Recommend_FallsBackToEmployeeDefault_WhenNoPreviousRate()
    {
        var init = new ClientRateInit
        {
            PreviousRate = 0, PreviousPrepaidRate = 0,
            DefaultRate = 200, DefaultPrepaidRate = 185
        };

        var rec = RateResolver.Recommend(init);

        rec.Source.Should().Be(RateSource.EmployeeDefault);
        rec.Rate.Should().Be(200);
        rec.PrepaidRate.Should().Be(185);
    }

    [Fact]
    public void Recommend_ReturnsNone_WhenNeitherRateAvailable()
    {
        var rec = RateResolver.Recommend(new ClientRateInit());

        rec.Source.Should().Be(RateSource.None);
        rec.Rate.Should().Be(0);
        rec.PrepaidRate.Should().Be(0);
    }

    // ── SellPriceFor ─────────────────────────────────────────────

    [Theory]
    [InlineData("B", 325)]
    [InlineData("W", 325)]
    [InlineData(null, 325)]
    [InlineData("BPP", 300)]
    [InlineData("bpp", 300)]
    public void SellPriceFor_UsesPrepaidOnlyForBpp(string? billable, decimal expected)
    {
        RateResolver.SellPriceFor(billable, rate: 325, prepaidRate: 300).Should().Be(expected);
    }

    // ── ExtendedExpiry ───────────────────────────────────────────

    [Fact]
    public void ExtendedExpiry_AddsMonthsFromGivenDate()
    {
        var from = new DateOnly(2026, 6, 26);

        RateResolver.ExtendedExpiry(from, 6).Should().Be(new DateOnly(2026, 12, 26));
        RateResolver.ExtendedExpiry(from).Should().Be(new DateOnly(2026, 12, 26)); // default 6 months
    }

    // ── IsActive ─────────────────────────────────────────────────

    [Fact]
    public void IsActive_TrueWhenExpiryOnOrAfterDate_OrNull()
    {
        var on = new DateOnly(2026, 6, 26);

        RateResolver.IsActive(new DateTime(2026, 6, 26), on).Should().BeTrue();   // same day
        RateResolver.IsActive(new DateTime(2026, 7, 1), on).Should().BeTrue();    // future
        RateResolver.IsActive(null, on).Should().BeTrue();                        // no expiry
        RateResolver.IsActive(new DateTime(2026, 6, 25), on).Should().BeFalse();  // expired yesterday
    }

    // ── BuildRecoveryOptions ─────────────────────────────────────

    [Fact]
    public void BuildRecoveryOptions_WithPreviousRateRow_OffersCreateAndInPlaceExtend()
    {
        var rec = new RateRecommendation(325, 300, RateSource.Previous);

        var options = RateResolver.BuildRecoveryOptions("NWIND", rec, previousRateId: 914937214, new DateOnly(2026, 6, 26));

        options.Should().HaveCount(2);
        options[0].Action.Should().Be("create");
        options[0].Command.Should().Be("tp rate create --client NWIND --rate 325 --prepaid 300 --yes");
        options[1].Action.Should().Be("extend");
        options[1].Command.Should().Be("tp rate update --client NWIND --id 914937214 --expiry 2026-12-26 --yes");
    }

    [Fact]
    public void BuildRecoveryOptions_WithNoPreviousRow_OffersCreateOnly()
    {
        var rec = new RateRecommendation(285, 270, RateSource.EmployeeDefault);

        var options = RateResolver.BuildRecoveryOptions("NWIND", rec, previousRateId: null, new DateOnly(2026, 6, 26));

        options.Should().ContainSingle();
        options[0].Action.Should().Be("create");
        options[0].Command.Should().Be("tp rate create --client NWIND --rate 285 --prepaid 270 --yes");
    }

    [Fact]
    public void BuildRecoveryOptions_WithNoRecommendation_UsesPlaceholderAmounts()
    {
        var rec = new RateRecommendation(0, 0, RateSource.None);

        var options = RateResolver.BuildRecoveryOptions("NWIND", rec, previousRateId: null, new DateOnly(2026, 6, 26));

        options.Should().ContainSingle();
        options[0].Command.Should().Be("tp rate create --client NWIND --rate <amount> --prepaid <amount> --yes");
    }
}
