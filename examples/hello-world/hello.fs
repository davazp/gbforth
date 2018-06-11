require gbhw.fs

title: EXAMPLE
gamecode: HELO
makercode: RX

code disable-interrupts
  di,
  ret,
end-code

code disable-lcd
  [rLCDC] A ld,
  rlca,

  #NC ret,

  here<
  [rLY] A ld,
  #145 # A cp,
  <there #NZ jr,
  [rLCDC] A ld,
  A #7 # res,
  A [rLCDC] ld,

  ret,
end-code


code cmovemono ( c-from c-to u -- )
  [C] ->A-> E ld,  C inc,
  [C] ->A-> D ld,  C inc,

  C inc, BC push,
  [C] ->A-> B ld,  C dec,
  [C] ->A-> C ld,

  begin, H|L->A, #NZ while,
    [BC] ->A-> [DE] ld,
    DE inc,             \ same as cmove but
    [BC] ->A-> [DE] ld, \ copies 2 bytes here
    BC inc,
    DE inc,

    HL dec,
  repeat,

  BC pop, C inc,
  ps-drop,
  ret,
end-code

[asm]

include memory.fs
label' TileData
TileData constant TileDataRef \ Workaround: label' not accessible in colon def
include ibm-font.fs



[host]
: %Title s" Hello World !" ;
[endhost]

( HACK: Don't use dmgforth internals here )
label' Title
Title constant TitleRef \ Workaround: label' not accessible in colon def
[host]
also dmgforth
%title rom-move
previous
[endhost]
ret,

%11 constant BLACK
%10 constant DARK_GREY
%01 constant LIGHT_GREY
%00 constant WHITE

: create-palette
  swap 2 lshift +
  swap 4 lshift +
  swap 6 lshift + ;

\ %11100100 constant DEFAULT_PALETTE  \ dark - %11 %10 %01 %00 - light

: set-palette rGBP c! ;

: set-scroll-position
  rSCY c!
  rSCX c! ;

: enable-lcd
  LCDCF_ON
  LCDCF_BG8000 or
  LCDCF_BG9800 or
  LCDCF_BGON or
  LCDCF_OBJ16 or
  LCDCF_OBJOFF or
  rLCDC c! ;

: clear-screen
  _SCRN0 SCRN_VX_B SCRN_VY_B * bl fill ;

: game-main
  disable-interrupts
  BLACK DARK_GREY LIGHT_GREY WHITE create-palette set-palette
  0 0 set-scroll-position
  disable-lcd
  TileDataRef _VRAM 256 8 * cmovemono
  enable-lcd
  clear-screen

  TitleRef
  _SCRN0 3 + SCRN_VY_B 7 * +
  [ [host] %Title nip [endhost] ] literal
  cmove ; \ should be CopyVRAM instead! -- not working without waitVram

main:

( program start )

' game-main # call,

begin, halt, repeat,

[endasm]
