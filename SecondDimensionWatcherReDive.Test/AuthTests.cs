namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class AuthTests
{
    [TestMethod]
    public void BCrypt_HashAndVerify_Matches()
    {
        const string password = "MySecurePassword123!";
        var hash = BCrypt.Net.BCrypt.HashPassword(password);

        Assert.IsTrue(BCrypt.Net.BCrypt.Verify(password, hash));
    }

    [TestMethod]
    public void BCrypt_WrongPassword_DoesNotMatch()
    {
        const string password = "MySecurePassword123!";
        var hash = BCrypt.Net.BCrypt.HashPassword(password);

        Assert.IsFalse(BCrypt.Net.BCrypt.Verify("WrongPassword", hash));
    }

    [TestMethod]
    public void BCrypt_DifferentHashesForSamePassword()
    {
        const string password = "MySecurePassword123!";
        var hash1 = BCrypt.Net.BCrypt.HashPassword(password);
        var hash2 = BCrypt.Net.BCrypt.HashPassword(password);

        Assert.AreNotEqual(hash1, hash2);
        Assert.IsTrue(BCrypt.Net.BCrypt.Verify(password, hash1));
        Assert.IsTrue(BCrypt.Net.BCrypt.Verify(password, hash2));
    }
}
