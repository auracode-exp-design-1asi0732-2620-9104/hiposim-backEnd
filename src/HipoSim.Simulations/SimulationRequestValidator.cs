using HipoSim.FinancialEngine;

namespace HipoSim.Simulations;

/// <summary>A request that passed validation, with defaults applied and the grace type parsed.</summary>
public sealed record ValidatedSimulation(
    double PropertyPrice,
    double DownPayment,
    double AnnualEffectiveRate,
    int TermInMonths,
    int GracePeriodMonths,
    GraceType GraceType,
    bool ApplyGoodPayerBonus,
    DateOnly? StartDate);

public sealed record SimulationValidationResult(
    ValidatedSimulation? Value,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => Value is not null;
}

/// <summary>Validation rules of TS06 (field names are the camelCase request properties).</summary>
public sealed class SimulationRequestValidator
{
    public const int MinimumTermInMonths = 60;
    public const int MaximumTermInMonths = 360;

    public SimulationValidationResult Validate(SimulationRequest request)
    {
        var errors = new Dictionary<string, List<string>>();

        void Fail(string field, string message)
        {
            if (!errors.TryGetValue(field, out var messages))
            {
                messages = [];
                errors[field] = messages;
            }

            messages.Add(message);
        }

        var propertyPrice = request.PropertyPrice;
        if (propertyPrice is null)
        {
            Fail("propertyPrice", "The propertyPrice field is required.");
        }
        else if (propertyPrice <= 0)
        {
            Fail("propertyPrice", "The propertyPrice must be greater than 0.");
            propertyPrice = null;
        }

        var downPayment = request.DownPayment;
        if (downPayment is null)
        {
            Fail("downPayment", "The downPayment field is required.");
        }
        else if (downPayment < 0)
        {
            Fail("downPayment", "The downPayment cannot be negative.");
            downPayment = null;
        }
        else if (propertyPrice is not null)
        {
            if (downPayment > propertyPrice)
            {
                Fail("downPayment", "The downPayment cannot be greater than the propertyPrice.");
            }
            else if ((decimal)downPayment.Value * 10m < (decimal)propertyPrice.Value)
            {
                Fail("downPayment", "The down payment must be at least 10% of the property price.");
            }
        }

        var rate = request.AnnualEffectiveRate;
        if (rate is null)
        {
            Fail("annualEffectiveRate", "The annualEffectiveRate field is required.");
        }
        else if (rate <= 0 || rate > 1)
        {
            Fail("annualEffectiveRate", "The annualEffectiveRate must be a fraction greater than 0 and at most 1 (0.085 means 8.5%).");
        }

        var term = request.TermInMonths;
        if (term is null)
        {
            Fail("termInMonths", "The termInMonths field is required.");
        }
        else if (term < MinimumTermInMonths || term > MaximumTermInMonths)
        {
            Fail("termInMonths", $"The termInMonths must be between {MinimumTermInMonths} and {MaximumTermInMonths}.");
            term = null;
        }

        var graceMonths = request.GracePeriodMonths ?? 0;
        var graceMonthsAreValid = graceMonths >= 0;
        if (!graceMonthsAreValid)
        {
            Fail("gracePeriodMonths", "The gracePeriodMonths cannot be negative.");
        }
        else if (term is not null && graceMonths >= term)
        {
            Fail("gracePeriodMonths", "The gracePeriodMonths must be lower than the termInMonths.");
            graceMonthsAreValid = false;
        }

        var graceType = GraceType.None;
        var graceTypeIsValid = true;
        switch (request.GraceType?.Trim().ToLowerInvariant())
        {
            case null or "" or "none":
                break;
            case "total":
                graceType = GraceType.Total;
                break;
            case "partial":
                graceType = GraceType.Partial;
                break;
            default:
                Fail("graceType", "The graceType must be one of: none, total, partial.");
                graceTypeIsValid = false;
                break;
        }

        if (graceTypeIsValid && graceMonthsAreValid)
        {
            if (graceType == GraceType.None && graceMonths > 0)
            {
                Fail("graceType", "The graceType must be 'total' or 'partial' when gracePeriodMonths is greater than 0.");
            }
            else if (graceType != GraceType.None && graceMonths == 0)
            {
                Fail("gracePeriodMonths", "The gracePeriodMonths must be greater than 0 when graceType is 'total' or 'partial'.");
            }
        }

        var errorMessages = errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        if (errors.Count > 0)
        {
            return new SimulationValidationResult(null, errorMessages);
        }

        var validated = new ValidatedSimulation(
            PropertyPrice: request.PropertyPrice!.Value,
            DownPayment: request.DownPayment!.Value,
            AnnualEffectiveRate: request.AnnualEffectiveRate!.Value,
            TermInMonths: request.TermInMonths!.Value,
            GracePeriodMonths: graceMonths,
            GraceType: graceType,
            ApplyGoodPayerBonus: request.ApplyGoodPayerBonus ?? false,
            StartDate: request.StartDate);

        return new SimulationValidationResult(validated, errorMessages);
    }
}
