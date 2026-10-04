namespace HipoSim.FinancialEngine;

public sealed record AmortizationRequest(
    double FinancedAmount,
    double MonthlyRate,
    int TermInMonths,
    DateOnly StartDate,
    GraceType GraceType = GraceType.None,
    int GraceMonths = 0,
    double BalloonRatio = 0,
    double MonthlyPropertyInsurance = 0,
    double MonthlyCreditLifeInsurance = 0);

public sealed record AmortizationEntry(
    int Period,
    DateOnly PaymentDate,
    double Installment,
    double Interest,
    double Amortization,
    double RemainingBalance,
    double PropertyInsurance,
    double CreditLifeInsurance,
    double TotalPayment,
    bool IsGracePeriod,
    bool IsBalloonPayment);

public sealed record AmortizationResult(
    IReadOnlyList<AmortizationEntry> Schedule,
    double RegularInstallment,
    double BalloonAmount);

/// <summary>
/// French method (ordinary arrears) with total/partial grace periods and an optional balloon payment.
/// Ported from AutoFinance Pro (Python); see the parity tests for the validation against the original.
/// </summary>
/// <remarks>
/// Grace: total capitalizes the period interest, partial pays only the interest.
/// Balloon: the regular installment amortizes only (1 - BalloonRatio) of the financed amount, interest is
/// still charged on the full balance, and the last installment settles whatever balance remains.
/// With BalloonRatio = 0 this is the plain French method.
/// </remarks>
public static class AmortizationCalculator
{
    /// <summary>R = C * i(1+i)^n / ((1+i)^n - 1); with a zero rate the capital is split evenly.</summary>
    public static double FrenchInstallment(double capital, double periodicRate, int periods)
    {
        if (periods <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(periods), periods, "The number of periods must be greater than 0.");
        }

        if (periodicRate == 0)
        {
            return capital / periods;
        }

        var factor = Math.Pow(1 + periodicRate, periods);
        return capital * (periodicRate * factor) / (factor - 1);
    }

    public static AmortizationResult BuildSchedule(AmortizationRequest request)
    {
        Validate(request);

        var rate = request.MonthlyRate;
        var insuranceProperty = request.MonthlyPropertyInsurance;
        var insuranceLife = request.MonthlyCreditLifeInsurance;
        var balance = request.FinancedAmount;
        var date = request.StartDate;
        var schedule = new List<AmortizationEntry>(request.TermInMonths);

        for (var month = 1; month <= request.GraceMonths; month++)
        {
            var interest = balance * rate;
            date = date.AddMonths(1);

            double installment;
            if (request.GraceType == GraceType.Total)
            {
                installment = 0.0;
                balance += interest;
            }
            else
            {
                installment = interest;
            }

            var total = installment + insuranceProperty + insuranceLife;
            schedule.Add(new AmortizationEntry(
                Period: month,
                PaymentDate: date,
                Installment: Rounding.Money(installment),
                Interest: Rounding.Money(interest),
                Amortization: 0.0,
                RemainingBalance: Rounding.Money(balance),
                PropertyInsurance: insuranceProperty,
                CreditLifeInsurance: insuranceLife,
                TotalPayment: Rounding.Money(total),
                IsGracePeriod: true,
                IsBalloonPayment: false));
        }

        var paymentMonths = request.TermInMonths - request.GraceMonths;
        var amortizableCapital = request.FinancedAmount * (1 - request.BalloonRatio);
        var regularInstallment = FrenchInstallment(amortizableCapital, rate, paymentMonths);
        var balloonAmount = 0.0;

        for (var k = 1; k <= paymentMonths; k++)
        {
            var interest = balance * rate;
            var amortization = regularInstallment - interest;
            balance -= amortization;
            var installment = regularInstallment;
            var isLast = k == paymentMonths;
            var isBalloon = false;

            if (isLast)
            {
                balloonAmount = Rounding.Money(balance);
                amortization += balance;
                installment = regularInstallment + balloonAmount;
                balance = 0.0;
                isBalloon = request.BalloonRatio > 0;
            }

            date = date.AddMonths(1);
            var total = installment + insuranceProperty + insuranceLife;
            schedule.Add(new AmortizationEntry(
                Period: request.GraceMonths + k,
                PaymentDate: date,
                Installment: Rounding.Money(installment),
                Interest: Rounding.Money(interest),
                Amortization: Rounding.Money(amortization),
                RemainingBalance: Rounding.Money(balance),
                PropertyInsurance: insuranceProperty,
                CreditLifeInsurance: insuranceLife,
                TotalPayment: Rounding.Money(total),
                IsGracePeriod: false,
                IsBalloonPayment: isBalloon));
        }

        return new AmortizationResult(schedule, Rounding.Money(regularInstallment), balloonAmount);
    }

    private static void Validate(AmortizationRequest request)
    {
        if (request.FinancedAmount <= 0)
        {
            throw new ArgumentException("The financed amount must be greater than 0.", nameof(request));
        }

        if (request.TermInMonths <= 0)
        {
            throw new ArgumentException("The term must be greater than 0 months.", nameof(request));
        }

        if (request.GraceMonths < 0 || request.GraceMonths >= request.TermInMonths)
        {
            throw new ArgumentException("The grace period must be between 0 and the term minus one month.", nameof(request));
        }

        if (request.GraceType == GraceType.None && request.GraceMonths > 0)
        {
            throw new ArgumentException("A grace type (Total or Partial) is required when grace months are greater than 0.", nameof(request));
        }

        if (request.GraceType != GraceType.None && request.GraceMonths == 0)
        {
            throw new ArgumentException("Grace months must be greater than 0 when a grace type is set.", nameof(request));
        }

        if (request.BalloonRatio < 0 || request.BalloonRatio >= 1)
        {
            throw new ArgumentException("The balloon ratio must be in the range [0, 1).", nameof(request));
        }

        if (request.MonthlyRate < 0)
        {
            throw new ArgumentException("The monthly rate cannot be negative.", nameof(request));
        }
    }
}
