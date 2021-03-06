require ./cpu.fs
require ./gbhw.fs
require ./memory.fs
require ./formatted-output.fs

variable cursor-x
variable cursor-y

: cursor-addr ( -- addr )
  _SCRN0 cursor-x @ +
  SCRN_VY_B cursor-y @ *
  + ;

: at-xy ( x y -- )
  cursor-y !
  cursor-x ! ;

\ Clear the screen
: page
  _SCRN0 [ SCRN_VX_B SCRN_VY_B * ]L bl fill
  0 0 at-xy ;

: cr
  0 cursor-x !
  1 cursor-y +! ;

: emit
  dup 10 = if
    0 cursor-x !
    1 cursor-y +!
  else
    ( n ) cursor-addr c!video
    1 cursor-x +!
  then ;

: space bl emit ;
: spaces 0 ?do space loop ;

: type ( addr u -- )
  tuck
  cursor-addr swap cmovevideo
  cursor-x +! ;

: typewhite nip spaces ;

: . <# #s #> type space ;

: ? c@ . ;

:m ."
  postpone s"
  postpone type
; immediate

: .r ( n1 n2 -- )
  swap <# #s #>
  ( width addr u )
  rot over - 0 max spaces
  type ;

: reset-palette
  %11100100 rBGP c! ;

: reset-window-scroll
  0 rSCX c!
  0 rSCY c! ;

: init-term
  disable-interrupts
  reset-palette
  reset-window-scroll
  enable-interrupts
;
