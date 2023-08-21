//#define STATS

using ChessChallenge.API;
using System;
using static System.Math;
#if STATS
using System.Linq;
#endif


//  -12 under Tyrant V6
// 983 Tokens Base

// TO-DO Later (round 2):
// Aspiration Windows               40 Tokens
// Killer Moves                     22 Tokens   (17 +- 14.6)

// To-do Additions
// Futility Pruning                 41 Tokens   (48.1 +- 23.7)
// LMP                              23 Tokens

// Span Test                        12 Tokens

// To-Do:
// Tune RFP
// Tune FP
// Tune LMR
// Save Tokens
//      -> Try Tyrant's Pesto Tables




public class PriorBot : IChessBot
{
    // 0 is INVALID, 1 is LOWERBOUND, 2 is UPPERBOUND, 4 is EXACT for bound
    record struct TT_Entry(ulong ZobristKey, Move move, int depth, int score, int bound);

    TT_Entry[] transpositionTable = new TT_Entry[0x400000];

    int[,,] historyTable;
    Move[] killers = new Move[1024];

    //int[] moveScores = new int[218];


    Board currentBoard;
    Timer currentTimer;

    Move moveToPlay;
    int maxTimeMilliSeconds, chosenDepth;

#if STATS
    int nodes, lookups, ttEntryCount, bestEval = 0;
    int maxHistory = 0, maxPlyFromRoot = 0;
#endif

    public Move Think(Board board, Timer timer)
    {
        /*int ttEntrySizeBytes = Marshal.SizeOf<TT_Entry>();
        Console.WriteLine("Size of transposition table: " + ((float)(ttEntrySizeBytes * (transpositionTable.Length)))/ 1024.0 / 1024.0);*/
#if STATS
        nodes = 0;
        lookups = 0;
        ttEntryCount = 0;
#endif

        currentBoard = board;
        currentTimer = timer;
        moveToPlay = board.GetLegalMoves()[0];
        historyTable = new int[2, 7, 64];

        maxTimeMilliSeconds = timer.MillisecondsRemaining / 30;

        for (chosenDepth = 1; ;)
        {

            int eval = searchBoard(chosenDepth++, 0, -30000000, 30000000, true);

            if (timer.MillisecondsElapsedThisTurn >= maxTimeMilliSeconds) break;

#if STATS
            bestEval = eval;
            DebugPrints();
#endif

        }
#if STATS
        Console.WriteLine("Max history Value is: " + maxHistory);
#endif


        return moveToPlay;
    }

    int searchBoard(int depth, int plyFromRoot, int alpha, int beta, bool nullMovePruningAllowed = true)
    {

        // Silly local method to save tokens
        int Search(int newAlpha, int R = 1, bool nullMovePruning = true) => -searchBoard(depth - R, plyFromRoot + 1, -newAlpha, -alpha, nullMovePruning);

        ulong currentKey = currentBoard.ZobristKey;

        TT_Entry ttEntry = transpositionTable[currentKey % 0x400000];


        int bestScore = -900000,
            historyIndex = plyFromRoot % 2,
            moveCount = 0,
            alphaOrig = alpha,
            ttEntryBound = ttEntry.bound,
            ttEntryScore = ttEntry.score,
            moveScore;

        bool notRoot = plyFromRoot > 0,
            pvNode = beta - alpha > 1,
            inCheck = currentBoard.IsInCheck(),
            futilePruning;

        //Extension Checks      // Move Check 
        if (inCheck) depth++;

        bool qsearch = depth <= 0;

        if (notRoot && currentBoard.IsRepeatedPosition())
            return 0;


        if (notRoot && ttEntry.ZobristKey == currentKey && ttEntry.depth >= depth && (
            ttEntryBound == 3 // exact score
                || ttEntryBound == 2 && ttEntryScore >= beta // lower bound, fail high
                || ttEntryBound == 1 && ttEntryScore <= alpha // upper bound, fail low
        ))
#if Stats
        {
            lookups++;
#endif
            return ttEntry.score;
#if Stats
        }
#endif

        int eval = Evaluate();

        if (qsearch)
        {
            bestScore = eval;
            if (bestScore >= beta) return bestScore;
            alpha = Max(alpha, bestScore);
        }
        else if (!pvNode && !inCheck)      // Pruning Techniques
        {
            int rfPruningMargin = 95 * depth;
            if (depth <= 5 && eval - rfPruningMargin >= beta)
                return eval - rfPruningMargin;
            if (nullMovePruningAllowed && depth > 1 && currentBoard.TrySkipTurn())
            {
                int nullMoveScore = Search(beta, 3 + depth / 5, false);
                currentBoard.UndoSkipTurn();

                if (nullMoveScore >= beta) return nullMoveScore;
            }
        }

        // 12 Tokens less than Span
        Move[] allMoves = currentBoard.GetLegalMoves(qsearch && !inCheck);
        int[] moveScores = new int[allMoves.Length];


        foreach (Move move in allMoves)
            moveScores[moveCount++] = -(move == ttEntry.move ? 33554432 :

            // MVVLVA
            move.IsCapture ? 2097152 * (int)move.CapturePieceType - (int)move.MovePieceType :

            //Killers
            killers[plyFromRoot] == move ? 1048576 :

            // History Heuristic
            historyTable[historyIndex, (int)move.MovePieceType, move.TargetSquare.Index]);
        Array.Sort(moveScores, allMoves);

        // Generate appropriate moves depending on whether we're in QSearch

        /*Span<Move> allMoves = stackalloc Move[218];
        currentBoard.GetLegalMovesNonAlloc(ref allMoves, qsearch && !inCheck);

        // Order moves in reverse order -> negative values are ordered higher hence the strange equations
        foreach (Move move in allMoves)
            moveScores[moveCount++] = -(
            // Hash move
            move == ttEntry.move ? 33554432 :
            // MVVLVA
            move.IsCapture ? 2097152 * (int)move.CapturePieceType - (int)move.MovePieceType :

            //Killers
            killers[plyFromRoot] == move ? 1048576 :

            // History Heuristic
            historyTable[historyIndex, (int)move.MovePieceType, move.TargetSquare.Index]);

        moveScores.AsSpan(0, allMoves.Length).Sort(allMoves);*/

        Move currentBestMove = Move.NullMove;
        futilePruning = depth <= 8 && (eval + 150 * depth) <= alpha;


        moveCount = 0;
        foreach (Move move in allMoves)
        {
            if (currentTimer.MillisecondsElapsedThisTurn >= maxTimeMilliSeconds) return 99999999;

            bool importantMoves = move.IsCapture || move.IsPromotion;

            // Extended Futility Pruning
            if (futilePruning && !importantMoves && moveCount > 0)
                break;

            currentBoard.MakeMove(move);

#if STATS
            nodes++;
#endif

            if (moveCount == 0 || qsearch) moveScore = Search(beta);
            else if ((moveScore = moveCount < 7 || depth <= 2 ?
                                    alpha + 1 :
                                    Search(alpha + 1, 3)) > alpha &&
                (moveScore = Search(alpha + 1)) > alpha)
                moveScore = Search(beta);

            currentBoard.UndoMove(move);


#if Stats
            if(plyFromRoot == 0)
            {
                Console.WriteLine(move + " " + moveScore);
            }
#endif

            if (moveScore > bestScore)
            {
                bestScore = moveScore;
                currentBestMove = move;

                if (plyFromRoot == 0) moveToPlay = move;

                alpha = Max(alpha, moveScore);

                if (alpha >= beta)
                {
                    if (!qsearch && !move.IsCapture)
                    {
                        historyTable[historyIndex, (int)move.MovePieceType, move.TargetSquare.Index] += depth * depth;
                        killers[plyFromRoot] = move;

#if STATS
                        maxHistory = Max(maxHistory, historyTable[historyIndex, (int)move.MovePieceType, move.TargetSquare.Index]);
#endif
                    }

                    break;
                }
            }

            moveCount++;

        }

#if STATS
        if (ttEntry.depth == 0) { 
            ttEntryCount++;
        }
#endif

        if (!qsearch && allMoves.Length == 0) return inCheck ? plyFromRoot - 900000 : 0;

        int boundType = bestScore >= beta ? 2 : bestScore > alphaOrig ? 3 : 1;

        transpositionTable[currentKey % 0x400000] = new TT_Entry(currentKey, currentBestMove, depth, bestScore, boundType);

        return bestScore;

    }

    //Evaluation Section

    // 64 bit conversion of the table
    int[] gamephaseInc = { 0, 1, 1, 2, 4, 0 };

    // Piece values: pawn, knight, bishop, rook, queen, king
    int[] pieceValues = { 100, 310, 330, 500, 1000, 10000, 94, 281, 297, 512, 936, 10000 };


    ulong[] pieceTables = { 9259542123273814144, 16357001140413309557, 8829195605423724908, 8254401669090808169, 7313418688563415655, 7384914337435197812, 6737222767702746730, 9259542123273814144, 11081220660097301, 3987876604305901423, 5889764664614570412, 8615829970516021910, 8323936949179225464, 7599697423020169584, 7154940515368202861, 1687519861085072745, 7170907476791297912, 7390528431378371153, 8117082644194436478, 8972740228098131838, 8830870139635010180, 9263780805260055178, 9552012216381055361, 6880781611516057963, 11577242485982338987, 11214168400861567660, 8904630919850999184, 7527071450275215468, 6658137190229968489, 6009895851999132511, 6084482357074295353, 7886789834650901350, 7241961428581395373, 7519176848743963830, 8318016057608023993, 7306369598957387393, 8603695488949846909, 8251286653394652805, 6735286635485691265, 9182408126746812750, 4582289962092626573, 11348908854965197411, 8617781306342151786, 8028920214486217308, 5728408685346316109, 8246770769504399717, 9333560970455320968, 8188824274210166926, 9259542123273814144, 18446744073709551615, 16061197207704294100, 11572154847021273489, 10198820791839654783, 9549736231487373176, 10198551484841362041, 9259542123273814144, 5069491205526864157, 7455822975978661964, 7524541400582942039, 8035431733674084462, 7960834280560624750, 7601372000551791722, 6227482657520642388, 7155491318799420992, 8244812703126220648, 8681963116054150258, 9401405507207856260, 9045915848980202370, 8828055359350013303, 8394015409149540721, 8245661556352184677, 7599658875216755567, 10199125451369908357, 10055849173233928323, 9765923324499426173, 9548631222335405954, 9477131093459761269, 8971306245150767216, 8825507717624329597, 8611590021041063020, 8617240532094257812, 8040227885603724928, 7820089199224394633, 9481933937386764708, 7970407823045732247, 8099037312892111493, 7667768075636792416, 6873735846648900695, 3917408671579997295, 8399651535887701899, 9984928491787627661, 8689300325139585667, 7961402723962161525, 7889615596832196471, 7310895313622170479, 5430896352595961941 };


    int getLocationScore(int pieceType, int square)
    {
        return pieceValues[pieceType] + (int)((pieceTables[(pieceType) * 8 + 7 - square / 8] >> (7 - square & 0b111) * 8) & 0xFF) - 128;
    }
    int Evaluate()
    {
        int mgScore = 0, egScore = 0, gamePhase = 0;

        for (int color = 0; color < 2; color++)
        {
            for (int pieceType = 0; pieceType < 6; pieceType++)
            {
                ulong pieceBB = currentBoard.GetPieceBitboard((PieceType)(pieceType + 1), color == 0);
                while (pieceBB != 0)
                {
                    int square = BitboardHelper.ClearAndGetIndexOfLSB(ref pieceBB) ^ (56 * color);

                    mgScore += getLocationScore(pieceType, square);
                    egScore += getLocationScore(pieceType + 6, square);

                    //Console.WriteLine("square: " + square + " type: " + pieceType  + " score: " + toAddScore + "\n");
                    gamePhase += gamephaseInc[pieceType];

                }
            }
            mgScore = -mgScore;
            egScore = -egScore;
        }

        return (mgScore * gamePhase + egScore * (24 - gamePhase)) / 24 * (currentBoard.IsWhiteToMove ? 1 : -1);
    }

#if STATS
    private void DebugPrints()
    {
        string timeString = "\x1b[37mtime\u001b[38;5;214m " + currentTimer.MillisecondsElapsedThisTurn + "ms\x1b[37m\x1b[0m";
        timeString += string.Concat(Enumerable.Repeat(" ", 38 - timeString.Length));

        string depthString = "\x1b[1m\u001b[38;2;251;96;27mdepth " + (chosenDepth) + " ply\x1b[0m";
        depthString += string.Concat(Enumerable.Repeat(" ", 38 - depthString.Length));

        string bestMoveString = "\x1b[0mbestmove\x1b[32m " + moveToPlay + "\x1b[37m";
        bestMoveString += string.Concat(Enumerable.Repeat(" ", 2));

        string bestEvalString = string.Format("\x1b[37meval\x1b[36m {0:0.00} \x1b[37m", bestEval);
        bestEvalString += string.Concat(Enumerable.Repeat(" ", 32 - bestEvalString.Length));

        string nodesString = "\x1b[37mnodes\x1b[35m " + nodes + "\x1b[37m";
        nodesString += string.Concat(Enumerable.Repeat(" ", 29 - nodesString.Length));

        string lookupsString = "\x1b[37mnps\x1b[34m " + (nodes / Max(1, currentTimer.MillisecondsElapsedThisTurn)) * 1000 + "\x1b[37m";
        lookupsString += string.Concat(Enumerable.Repeat(" ", 32 - lookupsString.Length));

        string tablesizeString = "tablesize\x1b[31m " + ttEntryCount + "\x1b[37m";
        tablesizeString += string.Concat(Enumerable.Repeat(" ", 33 - tablesizeString.Length));

        Console.WriteLine(string.Join(" ", new string[] { depthString, timeString, bestMoveString, bestEvalString, nodesString, lookupsString, tablesizeString}));
    }
#endif

}