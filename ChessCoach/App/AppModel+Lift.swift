import Foundation

@MainActor
extension AppModel {
    func clearLift() {
        guard lift != nil || liftCard != nil else { return }
        lift = nil
        liftCard = nil
    }

    func updateLift(placement: Placement, me: PieceColor, whiteAtBottom: Bool) {
        guard dragHints, let cur = record.current, cur.board != placement, isCoached(cur.turn, me: me),
              let piece = LiftedPiece.detect(last: cur, current: placement, held: Self.mouseDown) else { clearLift(); return }
        let faded = hintLevel == 0 && (store.profile(id: profileId(for: cur.turn, me: me))?.supportFaded ?? false)
        let key = "\(cur.fen)|\(piece.square)|\(faded)"
        guard lift?.key != key else { return }
        let unsafe = faded ? [] : piece.unsafeSquares(in: cur)
        let marks = unsafe.map { u -> OverlayState.Square in
            let s = Placement.screenSquare(file: u.file, rank: u.rank, whiteAtBottom: whiteAtBottom)
            return OverlayState.Square(col: s.col, row: s.row)
        }
        lift = LiftView(key: key, marks: marks)
        liftCard = LiftedPiece.summary(unsafe, piece: piece.piece).map { CoachCard(id: "lift|" + key, kind: .hint, title: $0) }
        if let ctx = hintCtx { renderHints(ctx) }
        else if let rect = lastBoardRect { overlay.show(boardInScreen: rect, state: OverlayState(unsafe: marks)) }
    }
}
