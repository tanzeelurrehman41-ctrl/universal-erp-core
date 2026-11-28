namespace UniversalErp.Application.Sales;

public static class CreditCheckEngine
{
    public static CreditCheckResult Evaluate(
        decimal creditLimit,
        decimal currentOutstanding,
        decimal uninvoicedOrdersTotal,
        decimal newOrderAmount)
    {
        if (newOrderAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newOrderAmount), "New order amount cannot be negative.");
        }

        var currentExposure = currentOutstanding + uninvoicedOrdersTotal;
        var projectedExposure = currentExposure + newOrderAmount;

        // Credit limit 0 means credit is not granted (cash-only/no credit facility) or unlimited depending on policy.
        // In Universal ERP: creditLimit == 0 with PaymentMethod == "credit" means NO credit allowed unless prepaid.
        if (creditLimit <= 0m)
        {
            if (newOrderAmount > 0m)
            {
                return new CreditCheckResult(
                    IsAllowed: false,
                    RequiresOverride: true,
                    CurrentExposure: currentExposure,
                    CreditLimit: 0m,
                    ExcessAmount: newOrderAmount,
                    Message: "Customer has no credit limit facility configured (cash-only terms). Credit sale requires authorized approval.");
            }

            return new CreditCheckResult(
                IsAllowed: true,
                RequiresOverride: false,
                CurrentExposure: currentExposure,
                CreditLimit: 0m,
                ExcessAmount: 0m,
                Message: "Order approved.");
        }

        if (projectedExposure <= creditLimit)
        {
            return new CreditCheckResult(
                IsAllowed: true,
                RequiresOverride: false,
                CurrentExposure: currentExposure,
                CreditLimit: creditLimit,
                ExcessAmount: 0m,
                Message: $"Credit check passed. Available credit after order: PKR {creditLimit - projectedExposure:N2}.");
        }

        var excess = projectedExposure - creditLimit;
        return new CreditCheckResult(
            IsAllowed: false,
            RequiresOverride: true,
            CurrentExposure: currentExposure,
            CreditLimit: creditLimit,
            ExcessAmount: excess,
            Message: $"CREDIT_LIMIT_EXCEEDED: Projected exposure PKR {projectedExposure:N2} exceeds approved limit PKR {creditLimit:N2} by PKR {excess:N2}.");
    }
}
