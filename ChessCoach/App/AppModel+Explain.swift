import Foundation

@MainActor
extension AppModel {
    func card(_ slot: Slot) -> CoachCard? { slot == .review ? reviewCard : suggestionCard }

    func mutate(_ slot: Slot, _ change: (inout CoachCard) -> Void) {
        guard var c = card(slot) else { return }
        change(&c)
        if slot == .review { reviewCard = c } else { suggestionCard = c }
    }

    func startExplanation(_ slot: Slot, _ job: ExplainJob, useCache: Bool = true) {
        tasks[slot]?.cancel()
        jobs[slot] = job
        guard llmConfigured else {
            mutate(slot) { $0.note = String(localized: "Set up an AI server in the settings (⌘,) for explanations.") }
            return
        }
        let profile = store.profile(id: job.profileId)
        let context = profile?.promptContext ?? PlayerContext.neutral
        let style = profile?.nextExplanationStyle().style ?? ExplanationStyle()
        let key = job.cardId + "|" + style.key
        let pid = job.profileId
        let finish: @MainActor (AppModel, String) -> Void = { model, text in
            model.mutate(slot) { $0.text = text; $0.isLoading = false; $0.style = style; $0.profileId = pid; $0.feedback = nil }
            if let pid { model.lastExplanation = (style, pid, Date()) }
            if let pid, let theme = job.themeKey { model.store.noteExplained(profileId: pid, theme: theme, style: style) }
        }
        if useCache, let cached = explanationCache[key] { finish(self, cached); return }
        guard let request = job.build(context, style) else { mutate(slot) { $0.isLoading = false }; return }
        mutate(slot) { $0.isLoading = true; $0.text = ""; $0.note = nil; $0.style = nil; $0.feedback = nil }
        tasks[slot] = stream(request, update: { [weak self] text, done in
            guard let self, self.card(slot)?.id == job.cardId else { return }
            guard done else { self.mutate(slot) { $0.text = text }; return }
            var final = text
            if let allowed = job.allowed {
                let checked = FactCheck.clean(text, allowed: allowed())
                if checked.removed > 0 { Log.write("Explanation: removed \(checked.removed) sentence(s) with invented moves") }
                final = checked.text ?? ""
            }
            if final.isEmpty { self.mutate(slot) { $0.isLoading = false; $0.note = String(localized: "No reliable explanation. Try “Explain again”.") }; return }
            self.explanationCache[key] = final
            if self.explanationCache.count > 80 { self.explanationCache.removeAll() }
            finish(self, final)
        }, failed: { [weak self] msg in
            guard let self, self.card(slot)?.id == job.cardId else { return }
            self.mutate(slot) { $0.isLoading = false; $0.note = msg }
        })
    }

    func feedback(cardId: String, _ value: ExplanationFeedback) {
        for slot in [Slot.review, .suggestion] {
            guard let c = card(slot), c.id == cardId, let style = c.style, let pid = c.profileId else { continue }
            store.update(pid) { $0.recordFeedback(value, usedStyle: style) }
            mutate(slot) { $0.feedback = value }
            updateInfoLines()
        }
    }

    func noteAskedAgain() {
        guard let last = lastExplanation else { return }
        store.update(last.profileId) { $0.recordFeedback(.askedAgain, usedStyle: last.style) }
        lastExplanation = nil
    }

    func panelCollapsed(_ collapsed: Bool) {
        guard collapsed, let last = lastExplanation, Date().timeIntervalSince(last.at) < 6 else { return }
        store.update(last.profileId) { $0.recordFeedback(.collapsedImmediately, usedStyle: last.style) }
        lastExplanation = nil
    }

    func explainAgain() {
        guard isActive else { return }
        if let ctx = hintCtx, suggestionCard?.kind == .hint, effectiveShowBest || hintLevel >= 3 {
            tasks[.suggestion]?.cancel()
            ideaTask?.cancel()
            ideaCache[ctx.fen] = nil
            ideaFen = nil
            suggestionCard = nil
            renderHints(ctx)
            prefetchIdea(ctx)
            return
        }
        let slot: Slot = (hintCtx != nil && (effectiveShowBest || hintLevel >= 4) && suggestionCard?.style != nil) ? .suggestion : .review
        guard let c = card(slot), let job = jobs[slot], c.id == job.cardId else { return }
        if let style = c.style, let pid = c.profileId { store.update(pid) { $0.recordFeedback(.askedAgain, usedStyle: style) } }
        lastExplanation = nil
        startExplanation(slot, job, useCache: false)
    }

    func ask(_ question: String) {
        let id = "question|" + question
        let title = String(localized: "Question")
        guard let placement = hintCtx?.position.board ?? record.current?.board ?? stablePlacement else {
            answerCard = CoachCard(id: id, kind: .answer, title: title, note: String(localized: "No position recognised yet."))
            return
        }
        guard llmConfigured else {
            answerCard = CoachCard(id: id, kind: .answer, title: title, note: String(localized: "Set up an AI server in the settings (⌘,) for answers."))
            return
        }
        noteAskedAgain()
        let color = hintCtx?.toMove ?? record.current?.turn ?? lastMe ?? .white
        let ctx = hintCtx
        let showsBest = effectiveShowBest || hintLevel >= 4
        let request = Coach.questionRequest(
            placement: placement, toMove: color, question: question,
            score: ctx?.result.score(.facts),
            bestMove: showsBest ? ctx.map { $0.position.board.describeMove($0.hints.move, .facts) } : nil,
            threat: showThreats ? ctx?.threat?.text(.facts) : nil,
            language: Language.englishName)
        var card = CoachCard(id: id, kind: .answer, title: String(localized: "Your question: \(question)"))
        card.isLoading = true
        answerCard = card
        panel.model.question = ""
        panel.model.isAsking = true
        questionTask?.cancel()
        questionTask = stream(request, update: { [weak self] text, done in
            guard let self, self.answerCard?.id == id else { return }
            self.answerCard?.text = text
            if done { self.answerCard?.isLoading = false; self.panel.model.isAsking = false }
        }, failed: { [weak self] msg in
            guard let self, self.answerCard?.id == id else { return }
            self.answerCard?.isLoading = false
            self.answerCard?.note = msg
            self.panel.model.isAsking = false
        })
    }

    func stream(_ request: Coach.ExplanationRequest,
                        update: @escaping @MainActor (String, Bool) -> Void,
                        failed: @escaping @MainActor (String) -> Void) -> Task<Void, Never> {
        let base = llmBase, token = llmToken, model = llmModel
        return Task {
            do {
                let final = try await LLMClient.explain(base: base, token: token, model: model, system: request.system,
                                                        user: request.user, maxTokens: request.maxTokens) { text in
                    update(text, false)
                }
                update(final, true)
            } catch is CancellationError {
            } catch let e as URLError where e.code == .cancelled {
            } catch {
                failed(String(localized: "Explanation failed: \(error.localizedDescription)"))
            }
        }
    }

    func completeText(_ request: Coach.ExplanationRequest, onText: @escaping @MainActor (String) -> Void) async throws -> String {
        try await LLMClient.explain(base: llmBase, token: llmToken, model: llmModel, system: request.system,
                                    user: request.user, maxTokens: request.maxTokens, onText: onText)
    }
}
