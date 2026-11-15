using System;

namespace UniversalErp.Application.HrPayroll;

public static class HrSecurityMasking
{
    /// <summary>
    /// Masks a 13-digit Pakistani CNIC for field-level privacy (e.g. 42101-*****67-1).
    /// Keeps first 5 digits and last 2 characters visible.
    /// </summary>
    public static string MaskCnic(string? cnic)
    {
        if (string.IsNullOrWhiteSpace(cnic)) return string.Empty;
        var clean = cnic.Trim();
        if (clean.Length <= 8) return "******";

        // If formatted as 42101-1234567-1 (15 chars)
        if (clean.Length == 15 && clean[5] == '-' && clean[13] == '-')
        {
            return $"{clean[..6]}*****{clean[11..]}";
        }

        // If raw 13 digits
        if (clean.Length == 13)
        {
            return $"{clean[..5]}*****{clean[10..]}";
        }

        return $"{clean[..Math.Min(4, clean.Length)]}****{clean[^Math.Min(2, clean.Length)..]}";
    }

    /// <summary>
    /// Masks bank account number, keeping only last 4 digits visible.
    /// </summary>
    public static string MaskBankAccount(string? accountNo)
    {
        if (string.IsNullOrWhiteSpace(accountNo)) return string.Empty;
        var clean = accountNo.Trim();
        if (clean.Length <= 4) return "****";
        return $"{new string('*', clean.Length - 4)}{clean[^4..]}";
    }

    /// <summary>
    /// Masks IBAN, keeping country code + 2 check digits and last 4 characters visible.
    /// </summary>
    public static string MaskIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return string.Empty;
        var clean = iban.Trim();
        if (clean.Length <= 8) return "********";
        return $"{clean[..4]}****************{clean[^4..]}";
    }

    /// <summary>
    /// Masks salary / monetary amounts for callers lacking 'payroll.salary.view' privilege.
    /// </summary>
    public static decimal MaskSalary(decimal amount, bool hasSalaryViewPermission)
    {
        return hasSalaryViewPermission ? amount : 0m;
    }
}
