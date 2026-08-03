using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Core.Starters;

/// <summary>
/// The labels the gallery offers to start from.
///
/// A blank canvas is the worst first screen a label designer can show: nothing on it says
/// how big a barcode should be, where a marker goes, or what a finished label looks like.
/// These are four real layouts and one tour of the element types, all of them ordinary
/// documents once created, all of them on real stock sizes.
///
/// Three of the four carry template markers with a sample value seeded for each, because
/// that is what these labels are for: the fields a data system fills are markers, the
/// captions beside them are not, and the canvas shows the samples so the design reads as a
/// label rather than as a row of delimiters. The carton label is the exception and states
/// its values literally, because what it demonstrates is the STRUCTURE of a GS1-128
/// payload, and a marker in the middle of an application identifier's value would teach
/// the wrong thing about the one starter whose data has rules.
/// </summary>
public static class StarterCatalog
{
    /// <summary>Every starter, in the order the gallery shows them.</summary>
    public static IReadOnlyList<StarterLabel> All { get; } =
    [
        Shipping(),
        Product(),
        AssetTag(),
        Carton(),
        ElementTour(),
    ];

    /// <summary>The tour of the element types, which is what "New from Sample" loaded
    /// before there was a gallery to pick from.</summary>
    public static StarterLabel Tour => All[^1];

    /// <summary>
    /// A 4 by 6 inch shipping label: the sender in small print, the recipient large enough
    /// to read across a warehouse, and the tracking number as a Code 128.
    /// </summary>
    private static StarterLabel Shipping() => new(
        "Shipping label",
        "The 4 x 6 in parcel label: sender in small print, recipient large, and a Code 128 "
        + "tracking barcode across the bottom.",
        101.6,
        152.4,
        sheet =>
        {
            sheet.Text("From caption", 5, 5, 3, "FROM");
            sheet.Field("From name", 5, 9, 3.5, "FROM_NAME", "Northwind Traders");
            sheet.Field("From street", 5, 13.5, 3.5, "FROM_STREET", "1200 Industrial Way");
            sheet.Field("From city", 5, 18, 3.5, "FROM_CITY", "Columbus OH 43215");
            sheet.Rule("Divider 1", 2, 24, 97.6, 0.4);

            sheet.Text("Ship to caption", 5, 27, 4, "SHIP TO");
            sheet.Field("To name", 5, 33, 7, "TO_NAME", "Sofia Almeida");
            sheet.Field("To street", 5, 42, 6, "TO_STREET", "Rua das Palmeiras 482");
            sheet.Field("To city", 5, 49.5, 6, "TO_CITY", "Sao Paulo SP");
            sheet.Field("To postcode", 5, 57, 6, "TO_POSTCODE", "04521-030");
            sheet.Rule("Divider 2", 2, 70, 97.6, 0.4);

            sheet.Text("Service caption", 5, 73, 3, "SERVICE");
            sheet.Field("Service", 5, 77, 6, "SERVICE", "EXPRESS");
            sheet.Text("Weight caption", 60, 73, 3, "WEIGHT");
            sheet.Field("Weight", 60, 77, 6, "WEIGHT", "12.4 kg");

            sheet.Text("Ship date caption", 5, 88, 3, "SHIP DATE");
            sheet.Field("Ship date", 5, 92, 5, "SHIP_DATE", "2026-08-02");
            sheet.Rule("Divider 3", 2, 100, 97.6, 0.4);

            sheet.Text("Tracking caption", 5, 104, 3, "TRACKING NUMBER");
            sheet.Sample("TRACKING", "9405511899223197428490");
            sheet.Barcode(
                "Tracking barcode",
                BarcodeSymbology.Code128,
                5,
                108,
                sheet.Marker("TRACKING"),
                barHeightMm: 25,
                moduleMm: 0.25);

            sheet.Sample("ORDER_REF", "SO-100482");
            sheet.Text("Order reference", 5, 142, 3.5, "Order " + sheet.Marker("ORDER_REF"));
        });

    /// <summary>A 2.25 by 1.25 inch retail shelf label: what it is, what it costs, and the
    /// EAN-13 that identifies it.</summary>
    private static StarterLabel Product() => new(
        "Product label",
        "Retail shelf label with a price and an EAN-13, set in far enough from the edge "
        + "that the barcode's quiet zone fits on the stock.",
        57.15,
        31.75,
        sheet =>
        {
            sheet.Field("Product name", 3, 2, 4.5, "PRODUCT", "Blue Widget");

            sheet.Sample("SKU", "4471");
            sheet.Text("SKU", 36, 2, 3, "SKU " + sheet.Marker("SKU"));

            sheet.Field("Variant", 3, 7.5, 3, "VARIANT", "Medium, pack of 4");
            sheet.Field("Price", 3, 11, 7, "PRICE", "24.90");

            // Twelve digits, not thirteen: the printer works the check digit out and prints
            // it, so a sample that carried one would only be a chance to get it wrong.
            sheet.Sample("EAN", "789123456789");
            sheet.Barcode(
                "EAN-13",
                BarcodeSymbology.Ean13,
                7,
                19.5,
                sheet.Marker("EAN"),
                barHeightMm: 7.5,
                moduleMm: 0.33);
        });

    /// <summary>A 2 by 1 inch asset tag: the identifier in print and in a QR code, so it
    /// can be read by a person or by a phone.</summary>
    private static StarterLabel AssetTag() => new(
        "Asset tag",
        "Equipment tag carrying the asset number twice, as text and as a QR code, so it "
        + "can be read by a person or by a phone.",
        50.8,
        25.4,
        sheet =>
        {
            sheet.Field("Owner", 3, 3, 3.5, "COMPANY", "Northwind");
            sheet.Text("Asset caption", 3, 8, 2.5, "ASSET");
            sheet.Field("Asset number", 3, 11, 4, "ASSET_ID", "AST-100482");
            sheet.Field("Department", 3, 17.5, 3, "DEPARTMENT", "Field Service");

            // The same marker the text carries, so one value fills both and the tag cannot
            // print a number that disagrees with the code beside it.
            sheet.Qr("Asset QR", 34.5, 4, sheet.Marker("ASSET_ID"), cellMm: 0.6);
        });

    /// <summary>
    /// A 4 by 3 inch carton label carrying a GS1-128.
    ///
    /// The values are literal rather than markers, and the human-readable line is a text
    /// field rather than the barcode's own interpretation line. Both are what a real GS1
    /// label does: the printed line shows the application identifiers in brackets, which
    /// the encoded data does not contain, so it is written out rather than derived.
    /// </summary>
    private static StarterLabel Carton() => new(
        "Carton label (GS1-128)",
        "A GS1-128 carrying GTIN, expiry and batch, with the bracketed human-readable line "
        + "written under it. Literal values, ready to swap for markers.",
        101.6,
        76.2,
        sheet =>
        {
            sheet.Text("Carton caption", 4, 5, 3, "CARTON");
            sheet.Text("Description", 4, 9, 7, "Product description");
            sheet.Text("Quantity", 4, 19, 4.5, "24 units");
            sheet.Rule("Divider", 4, 26, 93.6, 0.4);

            // Assembled rather than typed: the separators between a variable-length value
            // and what follows it are what make the payload read back as three fields
            // instead of one long one, and Gs1Payload is where that rule lives.
            string payload = Gs1Payload.Build(
            [
                new Gs1Field("01", "07350053850019"),
                new Gs1Field("17", "261231"),
                new Gs1Field("10", "A1234"),
            ]);

            sheet.Barcode(
                "GS1-128",
                BarcodeSymbology.Code128,
                6,
                30,
                payload,
                barHeightMm: 18,
                moduleMm: 0.25,
                interpretationLine: false);

            sheet.Text("Human readable 1", 6, 51, 4.5, "(01) 07350053850019");
            sheet.Text("Human readable 2", 6, 57.5, 4.5, "(17) 261231   (10) A1234");
            sheet.Rule("Footer rule", 4, 65, 93.6, 0.4);
            sheet.Text("Packed", 4, 68, 3.5, "Packed 2026-08-31");
        });

    /// <summary>
    /// One of each of the element types, at the sizes the old "New from Sample" used.
    ///
    /// Its box is a header panel rather than a border round the whole label, and that is
    /// not cosmetic: a hollow box's footprint is its whole rectangle, so a frame around the
    /// stock sits inside every barcode's quiet zone and reports a crowded symbol on a label
    /// where nothing is crowded.
    /// </summary>
    private static StarterLabel ElementTour() => new(
        "Element tour",
        "One of each to click on and edit: a box, a line of text, a Code 128 and a QR "
        + "code. Not a label anybody ships, a place to start learning the canvas.",
        100,
        60,
        sheet =>
        {
            sheet.Box("Header", 1.875, 1.875, 96.25, 12.5, 0.375);
            sheet.Text("Title", 6.25, 6.25, 7.5, "LabelForge");
            sheet.Barcode(
                "Barcode",
                BarcodeSymbology.Code128,
                6.25,
                21.25,
                "LF-000123",
                barHeightMm: 17.5,
                moduleMm: 0.375);
            sheet.Qr("QR", 75, 21.25, "https://labelforge.app", cellMm: 0.75);
        });
}
