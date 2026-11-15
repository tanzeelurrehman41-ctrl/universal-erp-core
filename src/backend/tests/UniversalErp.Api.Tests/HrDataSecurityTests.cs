using System;
using UniversalErp.Application.HrPayroll;
using Xunit;

namespace UniversalErp.Api.Tests;

public class HrDataSecurityTests
{
    [Fact]
    public void MaskCnic_FormattedAndRaw_MasksMiddleDigitsSafely()
    {
        // Standard formatted 15-char CNIC: 42101-1234567-1
        string masked = HrSecurityMasking.MaskCnic("42101-1234567-1");
        Assert.Equal("42101-*****67-1", masked);

        // Raw 13 digits: 4210112345671
        string rawMasked = HrSecurityMasking.MaskCnic("4210112345671");
        Assert.Equal("42101*****671", rawMasked);

        // Null or empty
        Assert.Equal(string.Empty, HrSecurityMasking.MaskCnic(null));
        Assert.Equal(string.Empty, HrSecurityMasking.MaskCnic("  "));
    }

    [Fact]
    public void MaskBankAccount_RetainsOnlyLast4Digits()
    {
        string masked = HrSecurityMasking.MaskBankAccount("01234567890123");
        Assert.Equal("**********0123", masked);

        // Short account
        Assert.Equal("****", HrSecurityMasking.MaskBankAccount("123"));
        Assert.Equal(string.Empty, HrSecurityMasking.MaskBankAccount(null));
    }

    [Fact]
    public void MaskIban_RetainsCountryCodeAndLast4Digits()
    {
        string masked = HrSecurityMasking.MaskIban("PK36HABB0001234567890123");
        Assert.Equal("PK36****************0123", masked);

        Assert.Equal(string.Empty, HrSecurityMasking.MaskIban(null));
    }

    [Fact]
    public void MaskSalary_WhenPermissionNotGranted_ReturnsZero()
    {
        decimal actualSalary = 125_000m;

        // When permission is held
        decimal authorized = HrSecurityMasking.MaskSalary(actualSalary, hasSalaryViewPermission: true);
        Assert.Equal(125_000m, authorized);

        // When permission is denied
        decimal unauthorized = HrSecurityMasking.MaskSalary(actualSalary, hasSalaryViewPermission: false);
        Assert.Equal(0m, unauthorized);
    }
}
