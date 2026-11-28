namespace UniversalErp.Application.Sales;

public static class ArAgingEngine
{
    public sealed record InvoiceAgingSlice(
        Guid InvoiceId,
        string InvoiceNumber,
        DateOnly InvoiceDate,
        DateOnly DueDate,
        decimal TotalAmount,
        decimal PaidAmount);

    public static CustomerArAgingSummary CalculateCustomerAging(
        Guid customerId,
        string customerCode,
        string customerName,
        string currencyCode,
        IReadOnlyList<InvoiceAgingSlice> openInvoices,
        decimal totalCreditNotes,
        decimal totalReceipts,
        DateOnly asOfDate)
    {
        decimal current = 0m;
        decimal d31To60 = 0m;
        decimal d61To90 = 0m;
        decimal over90 = 0m;
        decimal totalReceivable = 0m;
        int openInvoiceCount = 0;

        foreach (var inv in openInvoices)
        {
            var outstandingOnInvoice = inv.TotalAmount - inv.PaidAmount;
            if (outstandingOnInvoice <= 0m) continue;

            openInvoiceCount++;
            totalReceivable += outstandingOnInvoice;

            var daysPastDue = asOfDate.DayNumber - inv.DueDate.DayNumber;

            if (daysPastDue <= 30)
            {
                current += outstandingOnInvoice;
            }
            else if (daysPastDue <= 60)
            {
                d31To60 += outstandingOnInvoice;
            }
            else if (daysPastDue <= 90)
            {
                d61To90 += outstandingOnInvoice;
            }
            else
            {
                over90 += outstandingOnInvoice;
            }
        }

        var netOutstanding = Math.Max(0m, totalReceivable - totalCreditNotes);

        return new CustomerArAgingSummary(
            CustomerId: customerId,
            CustomerCode: customerCode,
            CustomerName: customerName,
            CurrencyCode: currencyCode,
            CurrentBalance: current,
            Days31To60: d31To60,
            Days61To90: d61To90,
            Over90Days: over90,
            TotalReceivable: totalReceivable,
            TotalCreditNotes: totalCreditNotes,
            TotalReceipts: totalReceipts,
            NetOutstanding: netOutstanding,
            OpenInvoiceCount: openInvoiceCount);
    }
}
