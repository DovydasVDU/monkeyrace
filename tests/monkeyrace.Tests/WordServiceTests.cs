public class WordServiceTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(120)]
    public void Generate_HasEnoughNonEmptyWords(int seconds)
    {
        var words = WordService.Generate(seconds).Split(' ');

        // net MaxWpm greičiu rašantis žaidėjas neturi pasiekti teksto pabaigos
        Assert.True(words.Length >= RaceService.MaxWpm * seconds / 60);
        Assert.DoesNotContain(words, word => word.Trim().Length == 0 || word != word.Trim());
    }
}
