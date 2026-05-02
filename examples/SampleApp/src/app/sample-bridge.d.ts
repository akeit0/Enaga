declare global {
  const sampleHostPanel: {
    currentGuess: string;
    readonly statusText: string;
    readonly boardSummary: string;
    readonly attemptsUsed: number;
    readonly remainingGuesses: number;
    readonly isSolved: boolean;
    readonly isRoundComplete: boolean;
    readonly rectSummary: string;
    setRect(left: number, top: number, width: number, height: number): void;
    attachHostNode(runtimeId: string): void;
    submitGuess(): void;
    resetGame(): void;
  };
}

export {};
