// Synthetic template fixture. Exercises Atak-style markers, directives, and comments.
// These lines are not standard ZPL and must not crash the renderer.
##@SET_PRINTER(1)##
##@PRINT_REGION(SEQUENCIA=1,BANDA=1)##
^XA
^CI28
^PW800
^LL600
^FO40,40^A0N,40,40^FD##PRODUCT_NAME##^FS
^FO40,100^A0N,30,30^FDLot: ##LOT_NUMBER##^FS
^BY3^FO40,160^BCN,120,Y,N,N^FD##BARCODE_VALUE##^FS
^FO520,160^BQN,2,6^FDMA,##QR_PAYLOAD##^FS
^XZ
