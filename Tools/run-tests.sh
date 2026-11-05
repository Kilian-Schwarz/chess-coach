#!/bin/zsh
cd "${0:A:h}/.."
out=build/tests
mkdir -p $out
export CHESSCOACH_LOG_DIR=$PWD/$out/logs
areas=("$@")
(( ${#areas} )) || areas=(Tools/tests/*/main.swift(N:h:t))
failed=0
for a in $areas; do
  echo "--- $a"
  rm -f $out/$a
  extra=(Tools/tests/$a/support/*.swift(N))
  swiftc -O ChessCoach/Core/*.swift $extra Tools/tests/$a/main.swift -o $out/$a 2>&1 | grep "error:"
  if [[ ! -x $out/$a ]]; then echo "BUILD FAILED: $a"; failed=1; continue; fi
  $out/$a || { echo "FAILED: $a"; failed=1; }
done
(( failed == 0 )) && echo "ALL TESTS OK" || { echo "TESTS FAILED"; exit 1; }
