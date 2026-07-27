// The idiom every corpus label with a stamp uses: clear the area with a white ^GB, then
// recall the graphic into it at the same origin. The erase is written with a width of
// zero and a thickness that fills the shape, which is what the driver emits, and it is
// the reason reading the colour matters: read as black it paints a slab over the stamp.
~DGSTAMP,24,3,FFFFFFF00003::::F00003FFFFFF
^XA
^CI28
^PW400
^LL300
^FO40,40^A0N,30,30^FDErase then stamp^FS
^FO40,100^GB0,8,24,W^FS
^FO40,100^XGSTAMP,1,1^FS
^XZ
