using RssReader.Domain;

namespace RssReader.Domain.Tests;

[TestClass]
public sealed class ProfileNameValidatorTests
{
    [DataTestMethod]
    [DataRow("reader42")]
    [DataRow("Mary Jane")]
    [DataRow("Zoë 2026")]
    public void Validate_AcceptsLettersNumbersAndSpaces(string name)
    {
        var result = ProfileNameValidator.Validate(name);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(name, result.NormalizedName);
    }

    [DataTestMethod]
    [DataRow(" RSS Reader ", "RSS Reader")]
    [DataRow("reader 7", "reader 7")]
    public void Validate_TrimsOuterWhitespace(string name, string expectedName)
    {
        var result = ProfileNameValidator.Validate(name);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(expectedName, result.NormalizedName);
    }

    [DataTestMethod]
    [DataRow("news_feed")]
    [DataRow("reader@example.com")]
    [DataRow("reader-1")]
    [DataRow("two\twords")]
    public void Validate_RejectsSpecialCharacters(string name)
    {
        var result = ProfileNameValidator.Validate(name);

        Assert.AreEqual(ProfileNameError.InvalidCharacters, result.Error);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Validate_RequiresAtLeastOneLetterOrNumber(string? name)
    {
        var result = ProfileNameValidator.Validate(name);

        Assert.AreEqual(ProfileNameError.Required, result.Error);
    }

    [DataTestMethod]
    [DataRow("Catalog Master")]
    [DataRow("catalog master")]
    [DataRow(" Catalog Master ")]
    public void Validate_RejectsReservedCatalogMasterName(string name)
    {
        var result = ProfileNameValidator.Validate(name);

        Assert.AreEqual(ProfileNameError.Reserved, result.Error);
    }
}