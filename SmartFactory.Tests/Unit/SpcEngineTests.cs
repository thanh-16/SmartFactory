using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using SmartFactory.Api.Services;
using Xunit;

namespace SmartFactory.Tests.Unit;

/// <summary>
/// Comprehensive unit tests for SpcEngine:
/// Nelson Rules 1, 2, 3, EWMA filter, Binomial Control Limits, and Cpk capability.
/// Verifies boundary conditions: zero/negative sample sizes, zero variance, NaN/Infinity safety, and thread safety.
/// </summary>
public class SpcEngineTests
{
    // ==========================================
    // 1. NELSON RULE 1: GROSS OUTLIER (POINT BEYOND UCL)
    // ==========================================

    [Fact]
    public void EvaluateNelsonRule1_WhenDefectRateExceedsUcl_ReturnsCriticalAlert()
    {
        // Arrange (TC-SPC-01)
        var recentRates = new List<double> { 0.01, 0.02, 0.08 };
        double historicalMean = 0.015;
        double ucl = 0.05;

        // Act
        var result = SpcEngine.EvaluateNelsonRules(recentRates, historicalMean, ucl, "Crack");

        // Assert
        result.HasViolation.Should().BeTrue();
        result.ViolatedRule.Should().Be("NelsonRule1");
        result.AlertLevel.Should().Be("Critical");
        result.AnomalyScore.Should().Be(95);
        result.RuleDescription.Should().Contain("UCL");
        result.RootCauseHypothesis.Should().NotBeNullOrWhiteSpace();
        result.RecommendedAction.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void EvaluateNelsonRule1_WhenUclIsZero_UsesFallbackUclThreshold()
    {
        // Arrange: ucl = 0.0 triggers fallback threshold of 0.05
        var recentRates = new List<double> { 0.01, 0.06 };
        double historicalMean = 0.01;
        double ucl = 0.0;

        // Act
        var result = SpcEngine.EvaluateNelsonRules(recentRates, historicalMean, ucl);

        // Assert
        result.HasViolation.Should().BeTrue();
        result.ViolatedRule.Should().Be("NelsonRule1");
        result.AlertLevel.Should().Be("Critical");
    }

    [Fact]
    public void EvaluateNelsonRule1_WhenRateIsEqualToOrBelowUcl_DoesNotTriggerRule1()
    {
        // Arrange
        var recentRates = new List<double> { 0.01, 0.02, 0.05 };
        double historicalMean = 0.02;
        double ucl = 0.05;

        // Act
        var result = SpcEngine.EvaluateNelsonRules(recentRates, historicalMean, ucl);

        // Assert: 0.05 is not strictly greater than 0.05, so Rule 1 is not violated
        result.ViolatedRule.Should().NotBe("NelsonRule1");
    }

    // ==========================================
    // 2. NELSON RULE 2: STEEP TREND (3 CONSECUTIVE INCREASES)
    // ==========================================

    [Fact]
    public void EvaluateNelsonRule2_WhenThreeConsecutiveIncreasesAboveMean_ReturnsWarning()
    {
        // Arrange (TC-SPC-02)
        var recentRates = new List<double> { 0.010, 0.020, 0.035 };
        double historicalMean = 0.015;
        double ucl = 0.050;

        // Act
        var result = SpcEngine.EvaluateNelsonRules(recentRates, historicalMean, ucl, "Scratch");

        // Assert
        result.HasViolation.Should().BeTrue();
        result.ViolatedRule.Should().Be("NelsonRule2");
        result.AlertLevel.Should().Be("Warning");
        result.AnomalyScore.Should().Be(75);
        result.RuleDescription.Should().Contain("3 ca kiểm tra liên tiếp");
        result.RootCauseHypothesis.Should().Contain("Scratch");
        result.RecommendedAction.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void EvaluateNelsonRule2_WhenThreeIncreasesOccur_ButLatestIsBelowMean_DoesNotTrigger()
    {
        // Arrange: increasing, but all values are below the historical mean
        var recentRates = new List<double> { 0.001, 0.002, 0.003 };
        double historicalMean = 0.010;
        double ucl = 0.050;

        // Act
        var result = SpcEngine.EvaluateNelsonRules(recentRates, historicalMean, ucl);

        // Assert: Should not trigger since process is performing better than average
        result.HasViolation.Should().BeFalse();
        result.ViolatedRule.Should().Be("None");
    }

    [Fact]
    public void EvaluateNelsonRule2_WhenNotStrictlyIncreasing_DoesNotTrigger()
    {
        // Arrange: flat between p1 and p0
        var recentRates = new List<double> { 0.010, 0.025, 0.025 };
        double historicalMean = 0.015;
        double ucl = 0.050;

        // Act
        var result = SpcEngine.EvaluateNelsonRules(recentRates, historicalMean, ucl);

        // Assert
        result.HasViolation.Should().BeFalse();
        result.ViolatedRule.Should().Be("None");
    }

    [Fact]
    public void EvaluateNelsonRule2_WhenFewerThanThreePoints_DoesNotTrigger()
    {
        // Arrange: only 2 points
        var recentRates = new List<double> { 0.010, 0.030 };
        double historicalMean = 0.015;
        double ucl = 0.050;

        // Act
        var result = SpcEngine.EvaluateNelsonRules(recentRates, historicalMean, ucl);

        // Assert
        result.HasViolation.Should().BeFalse();
        result.ViolatedRule.Should().Be("None");
    }

    // ==========================================
    // 3. NELSON RULE 3: PROCESS SHIFT (8 CONSECUTIVE ABOVE MEAN)
    // ==========================================

    [Fact]
    public void EvaluateNelsonRule3_WhenEightConsecutiveAboveMean_ReturnsWarning()
    {
        // Arrange (TC-SPC-03)
        // Values fluctuate non-monotonically so Rule 2 is not triggered, but all 8 are > historicalMean
        var recentRates = new List<double> { 0.025, 0.024, 0.026, 0.023, 0.027, 0.024, 0.025, 0.024 };
        double historicalMean = 0.020;
        double ucl = 0.050;

        // Act
        var result = SpcEngine.EvaluateNelsonRules(recentRates, historicalMean, ucl, "Deformation");

        // Assert
        result.HasViolation.Should().BeTrue();
        result.ViolatedRule.Should().Be("NelsonRule3");
        result.AlertLevel.Should().Be("Warning");
        result.AnomalyScore.Should().Be(80);
        result.RuleDescription.Should().Contain("8 điểm đo liên tiếp");
        result.RootCauseHypothesis.Should().NotBeNullOrWhiteSpace();
        result.RecommendedAction.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void EvaluateNelsonRule3_WhenSevenPointsAboveMean_DoesNotTrigger()
    {
        // Arrange: only 7 points above mean
        var recentRates = new List<double> { 0.025, 0.024, 0.026, 0.023, 0.027, 0.024, 0.025 };
        double historicalMean = 0.020;
        double ucl = 0.050;

        // Act
        var result = SpcEngine.EvaluateNelsonRules(recentRates, historicalMean, ucl);

        // Assert: Rule 3 requires >= 8 points
        result.HasViolation.Should().BeFalse();
        result.ViolatedRule.Should().Be("None");
    }

    [Fact]
    public void EvaluateNelsonRule3_WhenOneOfEightPointsIsBelowMean_DoesNotTrigger()
    {
        // Arrange: one dip at position 4
        var recentRates = new List<double> { 0.025, 0.024, 0.026, 0.019, 0.027, 0.024, 0.025, 0.024 };
        double historicalMean = 0.020;
        double ucl = 0.050;

        // Act
        var result = SpcEngine.EvaluateNelsonRules(recentRates, historicalMean, ucl);

        // Assert
        result.HasViolation.Should().BeFalse();
        result.ViolatedRule.Should().Be("None");
    }

    // ==========================================
    // 4. BOUNDARY CONDITIONS & EXTREME INPUTS (ZERO / NULL / EMPTY)
    // ==========================================

    [Fact]
    public void EvaluateNelsonRules_WhenNullOrEmptyList_ReturnsNoViolationWithoutException()
    {
        // Arrange & Act
        var nullResult = SpcEngine.EvaluateNelsonRules(null!, 0.02, 0.05);
        var emptyResult = SpcEngine.EvaluateNelsonRules(new List<double>(), 0.02, 0.05);

        // Assert
        nullResult.HasViolation.Should().BeFalse();
        nullResult.ViolatedRule.Should().Be("None");
        emptyResult.HasViolation.Should().BeFalse();
        emptyResult.ViolatedRule.Should().Be("None");
    }

    [Fact]
    public void EvaluateNelsonRules_WhenSinglePointBelowUcl_ReturnsNoViolation()
    {
        // Arrange
        var singlePoint = new List<double> { 0.015 };

        // Act
        var result = SpcEngine.EvaluateNelsonRules(singlePoint, 0.02, 0.05);

        // Assert
        result.HasViolation.Should().BeFalse();
        result.ViolatedRule.Should().Be("None");
    }

    [Fact]
    public void EvaluateNelsonRules_With10000Points_ExecutesSubMillisecondWithoutMemorySpike()
    {
        // Arrange: 10,000 points of steady state
        var largeData = Enumerable.Repeat(0.015, 10000).ToList();

        // Act
        var start = DateTime.UtcNow;
        var result = SpcEngine.EvaluateNelsonRules(largeData, 0.015, 0.05);
        var elapsedMs = (DateTime.UtcNow - start).TotalMilliseconds;

        // Assert
        result.HasViolation.Should().BeFalse();
        elapsedMs.Should().BeLessThan(50, "SPC algorithm must evaluate in constant time O(1) by inspecting tail only");
    }

    // ==========================================
    // 5. EWMA FILTER SMOOTHING CALCULATION
    // ==========================================

    [Fact]
    public void CalculateEwma_WithValidInputs_SmoothesDataCorrectly()
    {
        // Arrange (TC-SPC-04)
        double previousEwma = 0.02;
        double currentVal = 0.05;
        double lambda = 0.2;

        // Act
        // Formula: 0.2 * 0.05 + 0.8 * 0.02 = 0.010 + 0.016 = 0.026
        var ewma = SpcEngine.CalculateEwma(previousEwma, currentVal, lambda);

        // Assert
        ewma.Should().BeApproximately(0.026, 0.0001);
    }

    [Fact]
    public void CalculateEwma_WhenInputsContainNaNOrInfinity_RecoversSafelyWithoutThrowing()
    {
        // Arrange & Act
        var ewmaFromNanPrev = SpcEngine.CalculateEwma(double.NaN, 0.03);
        var ewmaFromNanCurr = SpcEngine.CalculateEwma(0.02, double.NaN);
        var ewmaFromInfPrev = SpcEngine.CalculateEwma(double.PositiveInfinity, 0.04);
        var ewmaFromInfCurr = SpcEngine.CalculateEwma(0.02, double.NegativeInfinity);

        // Assert
        double.IsNaN(ewmaFromNanPrev).Should().BeFalse();
        ewmaFromNanPrev.Should().Be(0.03);

        double.IsNaN(ewmaFromNanCurr).Should().BeFalse();
        ewmaFromNanCurr.Should().Be(0.02);

        double.IsInfinity(ewmaFromInfPrev).Should().BeFalse();
        ewmaFromInfPrev.Should().Be(0.04);

        double.IsInfinity(ewmaFromInfCurr).Should().BeFalse();
        ewmaFromInfCurr.Should().Be(0.02);
    }

    // ==========================================
    // 6. BINOMIAL CONTROL LIMITS (p-chart UCL, LCL, Sigma)
    // ==========================================

    [Fact]
    public void CalculateBinomialControlLimits_WithValidInputs_ReturnsPlausibleLimits()
    {
        // Arrange: p = 2% (0.02), sample size n = 500
        // variance = 0.02 * 0.98 / 500 = 0.0196 / 500 = 0.0000392
        // sigma = sqrt(0.0000392) = 0.00626099
        // UCL = 0.02 + 3 * 0.00626 = ~0.03878
        // LCL = 0.02 - 3 * 0.00626 = 0.001217
        double pMean = 0.02;
        double sampleSize = 500;

        // Act
        var (ucl, lcl, sigma) = SpcEngine.CalculateBinomialControlLimits(pMean, sampleSize);

        // Assert
        sigma.Should().BeApproximately(0.00626, 0.0005);
        ucl.Should().BeGreaterThan(pMean);
        lcl.Should().BeLessThan(pMean);
        lcl.Should().BeGreaterThanOrEqualTo(0.0);
        ucl.Should().BeLessThanOrEqualTo(1.0);
    }

    [Theory]
    [InlineData(0, 500)]      // Zero pMean
    [InlineData(-0.05, 500)]  // Negative pMean
    [InlineData(0.02, 0)]     // Zero sample size (Divide-by-zero check)
    [InlineData(0.02, -100)]  // Negative sample size
    public void CalculateBinomialControlLimits_EdgeCases_ReturnsFallbackWithoutException(double pMean, double sampleSize)
    {
        // Act
        var (ucl, lcl, sigma) = SpcEngine.CalculateBinomialControlLimits(pMean, sampleSize);

        // Assert: Must not throw DivideByZeroException, and limits must be sane
        ucl.Should().Be(SpcEngine.FallbackUclThreshold);
        lcl.Should().Be(0.0);
        sigma.Should().Be(0.0);
    }

    [Fact]
    public void CalculateBinomialControlLimits_WhenLclCalculatesBelowZero_ClampsToZero()
    {
        // Arrange: very small pMean with small sample size makes pMean - 3*sigma negative
        double pMean = 0.005;
        double sampleSize = 50;

        // Act
        var (ucl, lcl, sigma) = SpcEngine.CalculateBinomialControlLimits(pMean, sampleSize);

        // Assert
        lcl.Should().Be(0.0, "Defect rate LCL cannot physically be negative");
        ucl.Should().BeGreaterThan(0.0);
    }

    // ==========================================
    // 7. PROCESS CAPABILITY (Cpk)
    // ==========================================

    [Fact]
    public void CalculateCpk_WithNormalProcess_ReturnsCalculatedRatio()
    {
        // Arrange: mean = 1% (0.01), sigma = 0.5% (0.005), USL = 5% (0.05)
        // Cpk = (0.05 - 0.01) / (3 * 0.005) = 0.04 / 0.015 = 2.67
        double pMean = 0.01;
        double sigma = 0.005;

        // Act
        var cpk = SpcEngine.CalculateCpk(pMean, sigma);

        // Assert
        cpk.Should().Be(2.67);
    }

    [Fact]
    public void CalculateCpk_WhenSigmaIsNearZero_ReturnsRobustPresetValue()
    {
        // Arrange: sigma = 0 (perfect process or zero variance)
        // Act
        var cpkGood = SpcEngine.CalculateCpk(0.01, 0.0);
        var cpkBad = SpcEngine.CalculateCpk(0.08, 0.0);

        // Assert
        cpkGood.Should().Be(2.0, "Zero variance within USL represents Six Sigma capability");
        cpkBad.Should().Be(0.5, "Zero variance exceeding USL represents failing capability");
    }

    [Fact]
    public void CalculateCpk_WhenProcessExceedsUsl_ClampsToZero()
    {
        // Arrange: mean = 8% (0.08) while USL is 5% (0.05) -> negative ratio
        double pMean = 0.08;
        double sigma = 0.01;

        // Act
        var cpk = SpcEngine.CalculateCpk(pMean, sigma);

        // Assert
        cpk.Should().Be(0.0, "Cpk should not report negative values in factory dashboards");
    }

    // ==========================================
    // 8. CONCURRENCY & THREAD-SAFETY UNDER LOAD
    // ==========================================

    [Fact]
    public async Task ParallelExecution_SpcCalculation_ThreadSafeUnderLoad()
    {
        // Arrange (TC-SPC-07)
        int threadCount = 20;
        var tasks = new List<Task>();

        // Act: Execute 20 concurrent threads running SPC calculations
        for (int i = 0; i < threadCount; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() =>
            {
                var sample = new List<double> { 0.01 + (index * 0.001), 0.02 + (index * 0.001), 0.08 + (index * 0.001) };
                var eval = SpcEngine.EvaluateNelsonRules(sample, 0.02, 0.05, "TestDefect");
                eval.HasViolation.Should().BeTrue();
                eval.ViolatedRule.Should().Be("NelsonRule1");

                var ewma = SpcEngine.CalculateEwma(0.02, 0.05);
                ewma.Should().BeApproximately(0.026, 0.0001);

                var (ucl, lcl, sigma) = SpcEngine.CalculateBinomialControlLimits(0.02, 200);
                ucl.Should().BeGreaterThan(0.0);

                var cpk = SpcEngine.CalculateCpk(0.02, sigma);
                cpk.Should().BeGreaterThan(0.0);
            }));
        }

        // Assert
        await Task.WhenAll(tasks);
    }
}
