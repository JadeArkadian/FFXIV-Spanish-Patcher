using FFXIVSpanishPatcher.App.Services;
using Xunit;

namespace FFXIVSpanishPatcher.App.Tests;

public sealed class TranslationMilestoneServiceTests
{
    [Fact]
    public void EmbeddedDocument_IsPresentAndUsesSupportedMarkdown()
    {
        var document = new TranslationMilestoneService().Load();

        Assert.NotEmpty(document);
    }

    [Fact]
    public void Parse_AcceptsDocumentedSyntax()
    {
        const string markdown = """
            # Título

            Texto con **negrita**, _cursiva_, ~~tachado~~ y [enlace](https://example.com).

            - Uno
            - Dos

            > Cita

            ---

            | A | B |
            | - | - |
            | 1 | `dos` |

            ```text
            código
            ```
            """;

        var document = new TranslationMilestoneService().Parse(markdown);

        Assert.NotEmpty(document);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("![remota](https://example.com/image.png)")]
    [InlineData("[peligro](file:///etc/passwd)")]
    [InlineData("   ")]
    public void Parse_RejectsUnsafeOrEmptyContent(string markdown)
    {
        Assert.Throws<InvalidDataException>(() => new TranslationMilestoneService().Parse(markdown));
    }
}
