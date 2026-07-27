using LabelForge.Core.Fields;
using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// Field catalogs: the list of fields a label's data source provides, imported from
/// whatever that system exports. The reader is tolerant by design, and these tests hold
/// it against the three real export shapes that made it that way.
/// </summary>
public sealed class FieldCatalogTests
{
    /// <summary>The bare shape: a marker and a tab, nothing else.</summary>
    private const string BareExport =
        "##CHAVE_FATO##\t\r\n##FILIAL_CODIGO##\t\r\n##PEDIDO_DOCUMENTO_CODIGO##\t\r\n";

    /// <summary>A bullet, a marker, and one labelled column.</summary>
    private const string TypedExport =
        "- ##CODIGO_BARRAS##\t Tipo: String\r\n"
        + "- ##TEMPERATURA##\t Tipo: Nullable<Decimal>\r\n"
        + "- ##TABELA_NUTRICIONAL##\t Tipo: List<ProdutoTabelaNutricionalPrint>\r\n";

    /// <summary>Two labelled columns, one of which names the source column.</summary>
    private const string SourcedExport =
        "- ##FILIAL_NOME##\t Tipo: String\t Origem: tbFilial.nome_filial\r\n"
        + "- ##GS1_EAN_LIQUIDO_VALIDADE##\t Tipo: String\t Origem: tbProdutoRef.ean\r\n";

    [Fact]
    public void Reads_TheBareExportShape()
    {
        IReadOnlyList<FieldDefinition> fields = FieldListReader.Read(BareExport);

        Assert.Equal(
            ["CHAVE_FATO", "FILIAL_CODIGO", "PEDIDO_DOCUMENTO_CODIGO"],
            fields.Select(f => f.Name));
        Assert.All(fields, f => Assert.Equal(string.Empty, f.Type));
    }

    [Fact]
    public void Reads_TheTypedExportShape()
    {
        IReadOnlyList<FieldDefinition> fields = FieldListReader.Read(TypedExport);

        Assert.Equal(3, fields.Count);
        Assert.Equal("CODIGO_BARRAS", fields[0].Name);
        Assert.Equal("String", fields[0].Type);
        Assert.Equal("Nullable<Decimal>", fields[1].Type);
    }

    [Fact]
    public void Reads_TheSourcedExportShape()
    {
        IReadOnlyList<FieldDefinition> fields = FieldListReader.Read(SourcedExport);

        Assert.Equal("FILIAL_NOME", fields[0].Name);
        Assert.Equal("String", fields[0].Type);
        Assert.Equal("tbFilial.nome_filial", fields[0].Origin);
    }

    /// <summary>A collection-typed field is the one thing worth deriving from the type
    /// text, because it is written with an index in the marker.</summary>
    [Fact]
    public void ACollectionTypedField_IsFlaggedAsAList()
    {
        IReadOnlyList<FieldDefinition> fields = FieldListReader.Read(TypedExport);

        Assert.False(fields[0].IsList);
        Assert.True(fields[2].IsList);
    }

    [Fact]
    public void Prose_AndBlankLines_AreSkippedRatherThanReported()
    {
        IReadOnlyList<FieldDefinition> fields = FieldListReader.Read(
            "Campos disponiveis para esta etiqueta\r\n"
            + "\r\n"
            + "- ##CODIGO_BARRAS##\t Tipo: String\r\n"
            + "gerado em 26/07/2026\r\n");

        Assert.Equal("CODIGO_BARRAS", Assert.Single(fields).Name);
    }

    /// <summary>An export written with another system's delimiters reads just as well,
    /// which is the point of not hardcoding them.</summary>
    [Fact]
    public void Reads_AnExportInAnotherMarkerSyntax()
    {
        var syntax = new MarkerSyntax("{{", "}}", ':');

        IReadOnlyList<FieldDefinition> fields = FieldListReader.Read(
            "{{order_id}}\t\r\n{{customer_name}}\t\r\n", syntax);

        Assert.Equal(["order_id", "customer_name"], fields.Select(f => f.Name));
    }

    [Fact]
    public void AFieldListedTwice_IsOneField()
    {
        IReadOnlyList<FieldDefinition> fields = FieldListReader.Read(
            "##LOTE##\t Tipo: String\r\n##LOTE##\t Tipo: Int32\r\n");

        Assert.Equal("String", Assert.Single(fields).Type);
    }

    /// <summary>An export that lists a field with its format still describes the field.</summary>
    [Fact]
    public void AMarkersModifier_IsNotPartOfTheFieldName() =>
        Assert.Equal(
            "DATA_PRODUCAO",
            Assert.Single(FieldListReader.Read("##DATA_PRODUCAO@dd/MM/yyyy##\r\n")).Name);

    [Fact]
    public void AnUnknownMarker_IsNamedWithTheFieldItWasProbablyMeantToBe()
    {
        var catalog = new FieldCatalog("Abate", FieldListReader.Read(TypedExport));
        var document = new LabelDocument();
        document.Elements.Add(new TextElement { Text = "##CODIGO_BARAS##" });

        UnknownField unknown = Assert.Single(UnknownFieldCheck.Check(document, catalog));

        Assert.Equal("CODIGO_BARAS", unknown.Name);
        Assert.Equal("CODIGO_BARRAS", unknown.Suggestion);
    }

    /// <summary>
    /// The failure this feature exists for, taken from a real label: a tab character
    /// inside the tag name. Nothing rejects it, so the marker is never substituted and
    /// the label prints "##TABELA<tab>_NUTRICIONAL[1].PERCENTUAL##" as visible text.
    /// </summary>
    [Fact]
    public void ATagBrokenByAStrayCharacter_IsCaught()
    {
        var catalog = new FieldCatalog("Abate", FieldListReader.Read(TypedExport));
        var document = new LabelDocument();
        document.Elements.Add(new TextElement { Text = "##TABELA\t_NUTRICIONAL[1].PERCENTUAL##" });

        UnknownField unknown = Assert.Single(UnknownFieldCheck.Check(document, catalog));

        Assert.Contains("TABELA", unknown.Name, StringComparison.Ordinal);
    }

    /// <summary>A list field is addressed with an index and a member, and the catalog is
    /// asked about the field rather than about the whole expression.</summary>
    [Fact]
    public void AnIndexedListField_IsRecognised()
    {
        var catalog = new FieldCatalog("Abate", FieldListReader.Read(TypedExport));
        var document = new LabelDocument();
        document.Elements.Add(new TextElement { Text = "##TABELA_NUTRICIONAL[2].QUANTIDADE##" });

        Assert.Empty(UnknownFieldCheck.Check(document, catalog));
    }

    /// <summary>Counters and clocks are filled here, not by the data source, so they are
    /// not expected to be in a catalog of the data source's fields.</summary>
    [Fact]
    public void NoCatalog_MeansNoComplaints()
    {
        var document = new LabelDocument();
        document.Elements.Add(new TextElement { Text = "##ANYTHING_AT_ALL##" });

        Assert.Empty(UnknownFieldCheck.Check(document, null));
        Assert.Empty(UnknownFieldCheck.Check(document, FieldCatalog.Empty));
    }

    [Fact]
    public void Store_ReplacesACatalogOfTheSameNameOnReimport()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lf-catalogs-{Guid.NewGuid():N}.json");
        try
        {
            var store = new FieldCatalogStore(path);
            store.Add(new FieldCatalog("Etiqueta externa", FieldListReader.Read(BareExport)));

            FieldCatalogResult result = store.Add(
                new FieldCatalog("Etiqueta externa", FieldListReader.Read(TypedExport)));

            Assert.Null(result.Error);
            FieldCatalog saved = Assert.Single(result.Catalogs);
            Assert.Equal(3, saved.Fields.Count);
            Assert.True(saved.Contains("CODIGO_BARRAS"));

            // And it survives the round trip to disk.
            Assert.Equal(3, Assert.Single(new FieldCatalogStore(path).Load()).Fields.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Store_RefusesACatalogWithNothingInIt()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lf-catalogs-{Guid.NewGuid():N}.json");
        try
        {
            FieldCatalogResult result =
                new FieldCatalogStore(path).Add(new FieldCatalog("Empty", []));

            Assert.NotNull(result.Error);
            Assert.Empty(result.Catalogs);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Store_DegradesToNothingOnACorruptFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lf-catalogs-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ this is not json");
            Assert.Empty(new FieldCatalogStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The bug the catalogs uncovered, straight from a real label. With ^FH in force,
    /// "_BA" in CODIGO_BARRAS is a valid escape for 0xBA, so importing decoded it and
    /// left the marker as ##CODIGO(BA)RRAS##. The label is not broken: the system that
    /// fills markers in substitutes long before a printer sees the field, so a marker has
    /// to survive import verbatim.
    /// </summary>
    [Theory]
    [InlineData("^XA^FO10,10^A0N,30^FH^FDMA,##CODIGO_BARRAS##^FS^XZ", "MA,##CODIGO_BARRAS##")]
    [InlineData("^XA^FO10,10^A0N,30^FH^FD##SEXO_DESCRICAO##^FS^XZ", "##SEXO_DESCRICAO##")]
    [InlineData("^XA^FO10,10^A0N,30^FH^FD##GS1_EAN_LIQUIDO##^FS^XZ", "##GS1_EAN_LIQUIDO##")]
    public void HexEscaping_NeverReachesInsideAMarker(string zpl, string expected)
    {
        var text = Assert.IsType<TextElement>(
            Assert.Single(ZplDocumentImport.FromZpl(zpl, dpmm: 8).Document.Elements));

        Assert.Equal(expected, text.Text);
    }

    /// <summary>And the literal text of the same field still is unescaped, so the two
    /// rules cannot blur into each other. 0x82 is code page 850's accented e, which is
    /// what the printer's own default makes of that byte; the ^FH section of
    /// <see cref="EncodingTests"/> is where that rule lives.</summary>
    [Fact]
    public void HexEscaping_StillAppliesOutsideAMarker()
    {
        var text = Assert.IsType<TextElement>(Assert.Single(ZplDocumentImport
            .FromZpl("^XA^FO10,10^A0N,30^FH^FDMinist_82rio ##SIF_DIPOA##^FS^XZ", 8)
            .Document.Elements));

        Assert.Equal("Ministério ##SIF_DIPOA##", text.Text);
    }

    /// <summary>
    /// The real script shape, reduced from one of the sample files. The markers it has to
    /// produce are the ones the corpus labels already use, character for character.
    /// </summary>
    private const string Script = """
        public class Abate
        {
            public string maturidade(string COD_MATURIDADE)
            {
                switch (COD_MATURIDADE)
                {
                    case "M0":
                        break;
                    default:
                        return "F";
                }

                return "M";
            }

            public string CodMercado(string MERCADO_PRINCIPAL)
            {
                return MERCADO_PRINCIPAL;
            }
        }
        """;

    [Fact]
    public void Reads_TheCallsAScriptOffers()
    {
        IReadOnlyList<FieldFunction> functions = ScriptFunctionReader.Read(Script);

        Assert.Equal(2, functions.Count);
        Assert.Equal("Abate", functions[0].Owner);
        Assert.Equal("maturidade", functions[0].Name);
        Assert.Equal(["COD_MATURIDADE"], functions[0].Parameters);
    }

    /// <summary>The point of reading the signature: the marker comes out exactly as the
    /// real labels write it, parameter names and all.</summary>
    [Fact]
    public void AFunctionMarker_MatchesWhatTheLabelsAlreadyWrite()
    {
        IReadOnlyList<FieldFunction> functions = ScriptFunctionReader.Read(Script);

        Assert.Equal(
            "##@Abate.maturidade(COD_MATURIDADE)##",
            functions[0].Marker(MarkerSyntax.Default));
        Assert.Equal(
            "##@Abate.CodMercado(MERCADO_PRINCIPAL)##",
            functions[1].Marker(MarkerSyntax.Default));
    }

    [Fact]
    public void AMultiArgumentFunction_KeepsItsParameterOrder()
    {
        IReadOnlyList<FieldFunction> functions = ScriptFunctionReader.Read(
            "public class Data { public string MovVal(string DATA_PRODUCAO, string DATA_VALIDADE, string DATA_V1) { return DATA_V1; } }");

        Assert.Equal(
            "##@Data.MovVal(DATA_PRODUCAO,DATA_VALIDADE,DATA_V1)##",
            Assert.Single(functions).Marker(MarkerSyntax.Default));
    }

    /// <summary>Constructors and properties are not callable from a marker and must not
    /// be offered as though they were.</summary>
    [Fact]
    public void OnlyMethods_AreOffered()
    {
        IReadOnlyList<FieldFunction> functions = ScriptFunctionReader.Read(
            """
            public class Helper
            {
                public Helper() { }
                public string Name { get; set; }
                private string Hidden(string X) { return X; }
                public string Visible(string LOTE) { return LOTE; }
            }
            """);

        Assert.Equal("Visible", Assert.Single(functions).Name);
    }

    /// <summary>
    /// One import handles both kinds because a file tells which it is, and the telling
    /// has to happen at the file rather than a line at a time: "break;" splits into an
    /// identifier and an empty column, and so does a field-list row that ends in its
    /// delimiter. Only a script offers signatures, so a file that offers any is a script
    /// and its lines are never read as field names.
    /// </summary>
    [Fact]
    public void TheTwoKindsOfFile_TellThemselvesApart()
    {
        FieldCatalogImport script = FieldCatalogImport.Read(Script);
        FieldCatalogImport list = FieldCatalogImport.Read(TypedExport);

        Assert.Empty(script.Fields);
        Assert.Equal(2, script.Functions.Count);
        Assert.Equal("2 functions", script.Describe());

        Assert.Empty(list.Functions);
        Assert.Equal(3, list.Fields.Count);
        Assert.Equal("3 fields", list.Describe());
    }

    /// <summary>A script imported on its own is a catalog worth keeping: the field list
    /// it belongs with can arrive afterwards.</summary>
    [Fact]
    public void ACatalogOfOnlyFunctions_IsAccepted()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lf-catalogs-{Guid.NewGuid():N}.json");
        try
        {
            FieldCatalogResult result = new FieldCatalogStore(path).Add(
                new FieldCatalog("Abate", []) { Functions = ScriptFunctionReader.Read(Script) });

            Assert.Null(result.Error);
            Assert.Equal(2, Assert.Single(result.Catalogs).Functions.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FunctionsAndFields_BothSurviveTheStore()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lf-catalogs-{Guid.NewGuid():N}.json");
        try
        {
            var store = new FieldCatalogStore(path);
            store.Add(new FieldCatalog("Abate", FieldListReader.Read(TypedExport))
            {
                Functions = ScriptFunctionReader.Read(Script),
            });

            FieldCatalog saved = Assert.Single(new FieldCatalogStore(path).Load());

            Assert.Equal(3, saved.Fields.Count);
            Assert.Equal(2, saved.Functions.Count);
            Assert.Equal("Abate (3 fields, 2 functions)", saved.ToString());
            Assert.Equal(
                "One (1 field, 1 function)",
                new FieldCatalog("One", [saved.Fields[0]])
                {
                    Functions = [saved.Functions[0]],
                }.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A catalog saved before functions existed has none, rather than failing to
    /// load at all.</summary>
    [Fact]
    public void ACatalogSavedWithoutFunctions_StillLoads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lf-catalogs-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path,
                """[{"Name":"Older","Fields":[{"Name":"LOTE","Type":"String","Origin":"","IsList":false}]}]""");

            FieldCatalog loaded = Assert.Single(new FieldCatalogStore(path).Load());

            Assert.Equal("LOTE", Assert.Single(loaded.Fields).Name);
            Assert.Empty(loaded.Functions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A call is a directive, not a variable, so the unknown-marker check leaves
    /// it alone. That also keeps directives like ##@SET_PRINTER(2)## out of the warnings,
    /// which no catalog would ever list.</summary>
    [Fact]
    public void CallsAndDirectives_AreNotCheckedAgainstTheFieldList()
    {
        var catalog = new FieldCatalog("Abate", FieldListReader.Read(TypedExport));
        var document = new LabelDocument();
        document.Elements.Add(new TextElement { Text = "##@Abate.maturidade(COD_MATURIDADE)##" });
        document.Elements.Add(new TextElement { Text = "##@SET_PRINTER(2)##" });

        Assert.Empty(UnknownFieldCheck.Check(document, catalog));
    }

    /// <summary>The catalog is a design aid, so a label bound to one still generates the
    /// same bytes as a label bound to none.</summary>
    [Fact]
    public void TheBoundCatalog_NeverReachesTheZpl()
    {
        var document = new LabelDocument { WidthMm = 60, HeightMm = 40, Dpmm = 8 };
        document.Elements.Add(new TextElement { X = 10, Y = 10, Text = "##LOTE##" });
        string plain = new Core.Zpl.ZplGenerator().Generate(document);

        document.FieldCatalog = "Etiqueta externa";

        Assert.Equal(plain, new Core.Zpl.ZplGenerator().Generate(document));
        Assert.Equal(
            "Etiqueta externa",
            LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(document)).FieldCatalog);
    }
}
