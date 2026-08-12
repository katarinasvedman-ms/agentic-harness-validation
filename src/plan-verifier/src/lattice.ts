export function flowRankIsPermitted(
  sourceRank: number,
  destinationRank: number,
): boolean {
  //@ verify
  //@ requires sourceRank >= 0
  //@ requires sourceRank <= 4
  //@ requires destinationRank >= 0
  //@ requires destinationRank <= 4
  //@ ensures \result === (sourceRank <= destinationRank)
  return sourceRank <= destinationRank
}
