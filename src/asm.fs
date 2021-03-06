( Game Boy assembler )

( This module implements an assembler for the Game Boy architecture

Each Assembly operand is represented as a single cell in the
stack. The Z80 architecture supports 8 and 16 bits operands, but for
the host assembly system we require words of at least 32 bits, so high
bits of the words are used to tag the values with type information. )

require ./utils/bytes.fs
require ./utils/misc.fs
require ./sym.fs

[IFUNDEF] gb-assembler-impl
vocabulary gb-assembler
vocabulary gb-assembler-impl
vocabulary gb-assembler-emiters
[ENDIF]

\ We leave an extra copy of the gb-assembler dictionary, so it can be
\ replaced by [host] / [target].
: [asm] also gb-assembler also ;
: [endasm] previous previous ;

get-current
also gb-assembler
also gb-assembler-impl definitions
constant previous-wid

: [public]
  get-current
  also gb-assembler definitions ;

: [endpublic]
  previous set-current ;


( You can override this vectored words in order to customize where the
( assembler will write its output )

defer emit ( value -- )
defer offset ( -- offset )
defer emit-to ( value offset -- )

variable counter
:noname hex. 1 counter +! ; is emit
:noname counter @ ; is offset
:noname 2drop ; is emit-to

variable countcycles

$42 constant $xx
$4242 constant $xxxx

: ensure-short-jr { e -- e }
  e -128 >= e 127 <= and
  invert abort" The relative jump is out of range"
  e ;


: emit-rel-to ( n source-offset -- )
  dup >r 1+ - r> emit-to ;

: emit-16bits-to { n offset -- }
  n lower-byte   offset     emit-to
  n higher-byte  offset 1+  emit-to ;

( INSTRUCTION ARGUMENTS STACK

Instruction arguments are represented with two words [ value type ].

We define an auxiliary stack to hold the arguments for the next
aasembly instruction. Having a dedicate stack has some benefits like,
we will be able to override the instruction based on the number of
arguments provided. )

2 cells constant arg-size

create args 2 arg-size * allot
0 value args#

: flush-args 0 to args# ;

: check-no-args
  args# 0 <= abort" The instruction argument stack is empty" ;

: check-full-args
  args# 1 > abort" Too many arguments for an assembly instruction." ;

: push-arg ( value type -- )
  check-full-args
  swap args# arg-size * args + 2!
  args# 1+ to args# ;

: pop-arg ( -- value type )
  check-no-args
  args# 1- to args#
  args# arg-size * args + 2@ swap ;

: swap-args
  pop-arg pop-arg
  2swap
  push-arg push-arg ;

( Those words allow us to extract the argument value and type for the
  current instruction )
: arg1-value args 0 cells + @ ;
: arg1-type  args 1 cells + @ ;
: arg2-value args 2 cells + @ ;
: arg2-type  args 3 cells + @ ;

( for debugging )
: .args
  ." args# = " args# . CR
  ." arg1-value " arg1-value . CR
  ." arg1-type  " arg1-type  . CR
  ." arg2-value " arg2-value . CR
  ." arg2-type  " arg2-type  . CR ;



( Argument Types )

: begin-types 1 ;
: type dup constant 1 lshift ;
: end-types drop ;

begin-types
  type ~r
  type ~dd
  type ~qq
  type ~HL
  type ~SP
  type ~(BC)
  type ~(DE)
  type ~(HL)
  type ~(HL+)
  type ~(HL-)
  type ~(C)
  type ~t
  type ~b
  type ~n
  type ~nn
  type ~(n)
  type ~(nn)
  type ~A
  type ~cc
  type ~forward-reference
end-types

: ~e ~nn ;
: ~e_ ~n ;

: | or ;

: ~ss ~dd ;
: ~qq|dd ~dd ~qq | ;

: operand ( value type )
  create , , does> 2@ push-arg ;

[public]

( Define register operands )
%111 ~r ~A | operand A
%000 ~r      operand B
%001 ~r      operand C
%010 ~r      operand D
%011 ~r      operand E
%100 ~r      operand H
%101 ~r      operand L

%00 ~qq|dd operand BC
%01 ~qq|dd operand DE
%10 ~qq|dd ~HL | operand HL
%11    ~dd ~SP | operand SP
%11 ~qq    operand AF

%0   ~(BC)  operand [BC]
%0   ~(DE)  operand [DE]
%110 ~(HL)  operand [HL]
%0   ~(HL+) operand [HL+]
%0   ~(HL-) operand [HL-]
%0   ~(C)   operand [C]

( Define flag operands )
%00 ~cc operand #NZ
%01 ~cc operand #Z
%10 ~cc operand #NC
%11 ~cc operand #C

: invert-flag
  pop-arg
  swap %01 xor swap
  push-arg ;

( Push an immediate value to the arguments stack )
: #
  dup dup $38 <= swap $8 mod $0 = and if
    ~t ~b | ~n | ~nn | push-arg
  else
    dup %111 <= if
      ~b ~n | ~nn | push-arg
    else
      dup $FF <= if
        ~n ~nn | push-arg
      else
        ~nn push-arg
      then
    then
  then ;

: ]*
  dup $FF00 >= if
    ~(n) ~(nn) | push-arg
  else
    ~(nn) push-arg
  then ;


[endpublic]



( LABEL & REFERENCES )

-$ed7120 constant backward_mark
-$ed7121 constant forward_rel_ref
-$ed7122 constant forward_abs_ref

[public]

: there>
  ( This word doesn't know the proper offset yet, so it won't emit the
  ( forward reference. See `emit-addr` and `emit-rel-addr` instead. )
  0 ~nn ~forward-reference | push-arg ;

: patch-ref ( ref1 ref2 offset -- )
  -rot
  case
    forward_rel_ref of emit-rel-to endof
    forward_abs_ref of emit-16bits-to endof
    true abort" Expected a forward reference."
  endcase ;

: >here
  offset patch-ref ;

: named-ref>
  dup forward_rel_ref <> swap
  dup forward_abs_ref <> rot
  and abort" Expected a forward reference."
  CREATE 2, DOES> 2@ >here ;

: here<
  offset backward_mark ;

: <there
  backward_mark <> abort" Expected a backward reference."
  # ;

: label
  CREATE offset , DOES> @ # ;

' offset alias offset

[endpublic]

( Arguments pattern matching )

: type-match ( type type' -- bool )
  and 0<> ;

: 0arg-match? ( -- bool )
  args# 0 = ;

: 1arg-match? ( type -- bool )
  arg1-type type-match
  args# 1 = and ;

: 2arg-match? ( type1 type2 -- bool )
  arg2-type type-match swap
  arg1-type type-match and
  args# 2 = and ;

: args-match? ( ... #number-of-args -- bool )
  case
    0 of 0arg-match? endof
    1 of 1arg-match? endof
    2 of 2arg-match? endof
    abort" Unknown number of parameters"
  endcase ;


: begin-dispatch
  0
  ` depth
  ` >r
; immediate

: `#patterns ` depth ` r@ ` - ;

: ~~>
  1+ >r
  also gb-assembler-emiters
  `#patterns ` args-match? ` if
  r>
; immediate

: ::
  >r
  previous
  ` else
  r>
; immediate

: (unknown-args)
  flush-args
  true abort" Unknown parameters" ;

: end-dispatch
  ` (unknown-args)
  0 ?do ` then loop
  ` rdrop
; immediate


( Instructions helpers )

: ..  3 lshift ;
: op { prefix tribble1 tribble2 -- opcode }
  prefix .. tribble1 | .. tribble2 | ;

:  8lit, $ff and emit ;
: 16lit,
  dup lower-byte  emit
      higher-byte emit ;

ALSO GB-ASSEMBLER-EMITERS
DEFINITIONS

: b' arg2-value ;
: t  arg1-value $8 / ;
: r  arg1-value ;
: r' arg2-value ;
: dd0' arg2-value 1 lshift ;
: 0cc  arg1-value ;
: 0cc' arg2-value ;
: 1cc' 0cc' %100 + ;
: ss0 arg1-value 1 lshift ;
: ss1 ss0 %1 | ;
: qq0 arg1-value 1 lshift ;

: emit-addr ( arg-value arg-type )
  dup ~forward-reference type-match if
    2drop
    offset forward_abs_ref
    $xxxx 16lit,
  else
    drop 16lit,
  then ;

: emit-rel-addr ( arg-value arg-type )
  dup ~forward-reference type-match if
    2drop
    offset forward_rel_ref
    $xx 8lit,
  else
    drop offset 1+ - ensure-short-jr 8lit,
  then ;

: n,   arg1-value 8lit, ;
: n',  arg2-value 8lit, ;
: e,   arg1-value arg1-type emit-rel-addr ;
: e_,  n, ;
: nn,  arg1-value arg1-type emit-addr ;
: nn', arg2-value arg2-type emit-addr ;

: op, op emit ;
: cb-op, $cb emit op emit ;

PREVIOUS DEFINITIONS

: instruction :
  ` begin-dispatch ;

: end-instruction
  ` end-dispatch
  ` flush-args
  ` ;
; immediate


( INSTRUCTIONS )

: cycles countcycles +! ;

[public]

' countcycles alias countcycles

: 16lit, 16lit, ;
: 8lit,  8lit, ;

instruction adc,
  ~r    ~A  ~~> %10 %001    r op,       1 cycles ::
  ~(HL) ~A  ~~> %10 %001 %110 op,       2 cycles ::
  ~n    ~A  ~~> %11 %001 %110 op, n,    2 cycles ::
end-instruction

instruction add,
  ~r    ~A  ~~> %10 %000    r op,       1 cycles ::
  ~n    ~A  ~~> %11 %000 %110 op, n,    2 cycles ::
  ~(HL) ~A  ~~> %10 %000 %110 op,       2 cycles ::
  ~ss   ~HL ~~> %00 ss1  %001 op,       2 cycles ::
  ~e    ~SP ~~> %11 %101 %000 op, e,    4 cycles ::
end-instruction

instruction and,
  ~r    ~A  ~~> %10 %100    r op,       1 cycles ::
  ~(HL) ~A  ~~> %10 %100 %110 op,       2 cycles ::
  ~n    ~A  ~~> %11 %100 %110 op, n,    2 cycles ::
end-instruction

instruction bit,
  ~r    ~b  ~~> %01   b'    r cb-op,    2 cycles ::
  ~(HL) ~b  ~~> %01   b' %110 cb-op,    3 cycles ::
end-instruction

instruction call,
  ~nn       ~~> %11 %001 %101 op, nn,   6 cycles ::
  ~nn ~cc   ~~> %11 0cc' %100 op, nn,   6 cycles :: ( 3 if false )
end-instruction

instruction ccf,
            ~~> %00 %111 %111 op,       1 cycles ::
end-instruction

instruction cp,
  ~r    ~A  ~~> %10 %111    r op,       1 cycles ::
  ~(HL) ~A  ~~> %10 %111 %110 op,       2 cycles ::
  ~n    ~A  ~~> %11 %111 %110 op, n,    2 cycles ::
end-instruction

instruction cpl,
  ~A        ~~> %00 %101 %111 op,       1 cycles ::
end-instruction

instruction daa,
            ~~> %00 %100 %111 op,       1 cycles ::
end-instruction

instruction dec,
  ~r        ~~> %00 r    %101 op,       1 cycles ::
  ~(HL)     ~~> %00 %110 %101 op,       3 cycles ::
  ~ss       ~~> %00 ss1  %011 op,       2 cycles ::
end-instruction

instruction di,
            ~~> %11 %110 %011 op,       1 cycles ::
end-instruction

instruction ei,
            ~~> %11 %111 %011 op,       1 cycles ::
end-instruction

( Bug in game boy forces us to emit a NOP after halt, because HALT has
  an inconsistent skipping of the next instruction depending on if the
  interruptions are enabled or not )
instruction halt%,
            ~~> %01 %110 %110 op,       1 cycles ::
end-instruction

instruction inc,
  ~r        ~~> %00   r  %100 op,       1 cycles ::
  ~(HL)     ~~> %00 %110 %100 op,       3 cycles ::
  ~ss       ~~> %00 ss0  %011 op,       2 cycles ::
end-instruction

instruction jp,
  ~nn       ~~> %11 %000 %011 op, nn,   4 cycles ::
  ~nn  ~cc  ~~> %11 0cc' %010 op, nn,   4 cycles :: ( 3 if false )
  ~(HL)     ~~> %11 %101 %001 op,       1 cycles ::
end-instruction

instruction jr,
  ~e        ~~> %00 %011 %000 op, e,    3 cycles ::
  ~e ~cc    ~~> %00 1cc' %000 op, e,    3 cycles ::
end-instruction

instruction ld,
  ~r   ~r   ~~> %01 r'   r    op,       1 cycles ::
  ~n   ~r   ~~> %00 r'   %110 op, n,    2 cycles ::

  ~(HL) ~r  ~~> %01   r' %110 op,       2 cycles ::
  ~r  ~(HL) ~~> %01 %110    r op,       2 cycles ::
  ~n  ~(HL) ~~> %00 %110 %110 op, n,    3 cycles ::

  ~(BC) ~A  ~~> %00 %001 %010 op,       2 cycles ::
  ~(DE) ~A  ~~> %00 %011 %010 op,       2 cycles ::

  ~(C) ~A   ~~> %11 %110 %010 op,       2 cycles ::
  ~A  ~(C)  ~~> %11 %100 %010 op,       2 cycles ::

  ~(n) ~A   ~~> %11 %110 %000 op, n,    3 cycles ::
  ~A   ~(n) ~~> %11 %100 %000 op, n',   3 cycles ::
  ~(nn) ~A  ~~> %11 %111 %010 op, nn,   4 cycles ::
  ~A  ~(nn) ~~> %11 %101 %010 op, nn',  4 cycles ::

  ~(HL+) ~A ~~> %00 %101 %010 op,       2 cycles ::
  ~(HL-) ~A ~~> %00 %111 %010 op,       2 cycles ::

  ~A  ~(BC) ~~> %00 %000 %010 op,       2 cycles ::
  ~A  ~(DE) ~~> %00 %010 %010 op,       2 cycles ::

  ~A ~(HL+) ~~> %00 %100 %010 op,       2 cycles ::
  ~A ~(HL-) ~~> %00 %110 %010 op,       2 cycles ::

  ~nn  ~dd  ~~> %00 dd0' %001 op, nn,   3 cycles ::

  ~HL  ~SP  ~~> %11 %111 %001 op,       2 cycles ::

  ~e_  ~HL  ~~> %11 %111 %000 op, e_,   3 cycles :: \ equal to ldhl,

  ~SP ~(nn) ~~> %00 %001 %000 op, nn',  5 cycles ::
end-instruction

instruction ldhl,
  ~e_  ~SP  ~~> %11 %111 %000 op, e_,   3 cycles ::
end-instruction

instruction nop,
            ~~> %00 %000 %000 op,       1 cycles ::
end-instruction

instruction or,
  ~r    ~A  ~~> %10 %110    r op,       1 cycles ::
  ~(HL) ~A  ~~> %10 %110 %110 op,       2 cycles ::
  ~n    ~A  ~~> %11 %110 %110 op, n,    2 cycles ::
end-instruction

instruction pop,
  ~qq       ~~> %11 qq0 %001 op,        3 cycles ::
end-instruction

instruction push,
  ~qq       ~~> %11 qq0 %101 op,        4 cycles ::
end-instruction

instruction res,
  ~r    ~b  ~~>  %10   b'    r cb-op,   2 cycles ::
  ~(HL) ~b  ~~>  %10   b' %110 cb-op,   4 cycles ::
end-instruction

instruction ret,
        ~cc ~~> %11  0cc %000 op,       4 cycles ::
            ~~> %11 %001 %001 op,       4 cycles ::
end-instruction

instruction reti,
            ~~> %11 %011 %001 op,       4 cycles ::
end-instruction

instruction rl,
  ~r        ~~> %00 %010    r cb-op,    2 cycles ::
  ~(HL)     ~~> %00 %010 %110 cb-op,    4 cycles ::
end-instruction

instruction rla,
            ~~> %00 %010 %111 op,       1 cycles ::
end-instruction

instruction rlc,
  ~r        ~~> %00 %000    r cb-op,    2 cycles ::
  ~(HL)     ~~> %00 %000 %110 cb-op,    4 cycles ::
end-instruction

instruction rlca,
            ~~> %00 %000 %111 op,       1 cycles ::
end-instruction

instruction rr,
  ~r        ~~> %00 %011    r cb-op,    2 cycles ::
  ~(HL)     ~~> %00 %011 %110 cb-op,    4 cycles ::
end-instruction

instruction rra,
            ~~> %00 %011 %111 op,       1 cycles ::
end-instruction

instruction rrc,
  ~r        ~~> %00 %001    r cb-op,    2 cycles ::
  ~(HL)     ~~> %00 %001 %110 cb-op,    4 cycles ::
end-instruction

instruction rrca,
            ~~> %00 %001 %111 op,       1 cycles ::
end-instruction

instruction rst,
  ~t        ~~> %11    t %111 op,       4 cycles ::
end-instruction

instruction sbc,
  ~r    ~A  ~~> %10 %011    r op,       1 cycles ::
  ~(HL) ~A  ~~> %10 %011 %110 op,       2 cycles ::
  ~n    ~A  ~~> %11 %011 %110 op, n,    2 cycles ::
end-instruction

instruction scf,
            ~~> %00 %110 %111 op,       1 cycles ::
end-instruction

instruction set,
  ~r    ~b  ~~> %11   b'    r cb-op,    2 cycles ::
  ~(HL) ~b  ~~> %11   b' %110 cb-op,    4 cycles ::
end-instruction

instruction sla,
  ~r        ~~> %00 %100    r cb-op,    2 cycles ::
  ~(HL)     ~~> %00 %100 %110 cb-op,    4 cycles ::
end-instruction

instruction sra,
  ~r        ~~> %00 %101    r cb-op,    2 cycles ::
  ~(HL)     ~~> %00 %101 %110 cb-op,    4 cycles ::
end-instruction

instruction srl,
  ~r        ~~> %00 %111    r cb-op,    2 cycles ::
  ~(HL)     ~~> %00 %111 %110 cb-op,    4 cycles ::
end-instruction

instruction stop,
            ~~> %00 %010 %000 op,
                %00 %000 %000 op,       1 cycles ::
end-instruction

instruction sub,
  ~r    ~A  ~~> %10 %010    r op,       1 cycles ::
  ~(HL) ~A  ~~> %10 %010 %110 op,       2 cycles ::
  ~n    ~A  ~~> %11 %010 %110 op, n,    2 cycles ::
end-instruction

instruction swap,
  ~r        ~~> %00 %110    r cb-op,    2 cycles ::
  ~(HL)     ~~> %00 %110 %110 cb-op,    4 cycles ::
end-instruction

instruction xor,
  ~r    ~A  ~~> %10 %101    r op,       1 cycles ::
  ~(HL) ~A  ~~> %10 %101 %110 op,       2 cycles ::
  ~n    ~A  ~~> %11 %101 %110 op, n,    2 cycles ::
end-instruction

( Prevent the halt bug by emitting a NOP right after halt )
: halt, halt%, nop, ;


( Labelless Control Flow )

\ if...else...then

: if, invert-flag there> swap-args jr, ;
: else, there> jr, 2swap >here ;
: then, >here ;

\ begin...while...repeat

: begin,
  here< 0 0 ;

: while,
  or 0<> abort" WHILE, can only be used with BEGIN,"
  if, ;

: repeat,
  2swap
  <there jr,
  dup if then, else 2drop then ;

: again,
  or 0<> abort" AGAIN, can only be used with BEGIN,"
  0 0 repeat, ;

: until,
  or 0<> abort" UNTIL, can only be used with BEGIN,"
  invert-flag <there swap-args jr, ;


[endpublic]


previous-wid set-current
previous previous
