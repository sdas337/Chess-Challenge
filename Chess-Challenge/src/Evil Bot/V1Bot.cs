//#define STATS

using ChessChallenge.API;
using System;
using System.Linq;


// Current Token Count is 871
// With PVS is 923                  (36.8 +- 15)                     52 Tokens
// Check Extension tackes to 931    (46 +- 23)                        8 Tokens
// Null Move Pruning goes to 995   (102 +- 36.8)                     61 Tokens      135.8 +- 43.7
// Killer Moves makes minimal impact. Not including

// History Moves goes to 1061       (76 +- 30.9)                    66 Tokens




public class V1Bot : IChessBot
{
    struct TT_Entry
    {
        public ulong ZobristKey;
        public Move move;
        public int depth, score, bound;

        // 0 is INVALID, 1 is LOWERBOUND, 2 is UPPERBOUND, 4 is EXACT
        public TT_Entry(ulong _ZobristKey, Move _move, int _depth, int _score, int _bound)
        {
            (ZobristKey, move, depth, score, bound) = (_ZobristKey, _move, _depth, _score, _bound);
        }

    };

    const ulong ttMask = 0xFFFFF;
    TT_Entry[] transpositionTable = new TT_Entry[ttMask + 1];

    int[,,] historyTable;

    Move moveToPlay;
    int bestEval = 0, maxTimeMilliSeconds, chosenDepth;

#if STATS
    int nodes, lookups, ttEntryCount;
#endif

    // 64 bit conversion of the table
    int[] gamephaseInc = { 0, 1, 1, 2, 4, 0 };

    // Piece values: pawn, knight, bishop, rook, queen, king
    int[] pieceValues = { 100, 310, 330, 500, 1000, 10000, 94, 281, 297, 512, 936, 10000 };

    Board currentBoard;
    Timer currentTimer;

    ulong[] pieceTables = { 9259542123273814144, 16357001140413309557, 8829195605423724908, 8254401669090808169, 7313418688563415655, 7384914337435197812, 6737222767702746730, 9259542123273814144, 11081220660097301, 3987876604305901423, 5889764664614570412, 8615829970516021910, 8323936949179225464, 7599697423020169584, 7154940515368202861, 1687519861085072745, 7170907476791297912, 7390528431378371153, 8117082644194436478, 8972740228098131838, 8830870139635010180, 9263780805260055178, 9552012216381055361, 6880781611516057963, 11577242485982338987, 11214168400861567660, 8904630919850999184, 7527071450275215468, 6658137190229968489, 6009895851999132511, 6084482357074295353, 7886789834650901350, 7241961428581395373, 7519176848743963830, 8318016057608023993, 7306369598957387393, 8603695488949846909, 8251286653394652805, 6735286635485691265, 9182408126746812750, 4582289962092626573, 11348908854965197411, 8617781306342151786, 8028920214486217308, 5728408685346316109, 8246770769504399717, 9333560970455320968, 8188824274210166926, 9259542123273814144, 18446744073709551615, 16061197207704294100, 11572154847021273489, 10198820791839654783, 9549736231487373176, 10198551484841362041, 9259542123273814144, 5069491205526864157, 7455822975978661964, 7524541400582942039, 8035431733674084462, 7960834280560624750, 7601372000551791722, 6227482657520642388, 7155491318799420992, 8244812703126220648, 8681963116054150258, 9401405507207856260, 9045915848980202370, 8828055359350013303, 8394015409149540721, 8245661556352184677, 7599658875216755567, 10199125451369908357, 10055849173233928323, 9765923324499426173, 9548631222335405954, 9477131093459761269, 8971306245150767216, 8825507717624329597, 8611590021041063020, 8617240532094257812, 8040227885603724928, 7820089199224394633, 9481933937386764708, 7970407823045732247, 8099037312892111493, 7667768075636792416, 6873735846648900695, 3917408671579997295, 8399651535887701899, 9984928491787627661, 8689300325139585667, 7961402723962161525, 7889615596832196471, 7310895313622170479, 5430896352595961941 };


    int getLocationScore(int pieceType, int square)
    {
        return (int)((pieceTables[(pieceType) * 8 + 7 - square / 8] >> (7 - square & 0b111) * 8) & 0xFF) - 128;
    }

    //Current Distance into the future 5 moves is comfortable

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
        moveToPlay = Move.NullMove;
        historyTable = new int[2, 7, 64];

        maxTimeMilliSeconds = timer.MillisecondsRemaining / 30;

        //Console.WriteLine("Current Board Score: " + Evaluate() + " " + getLocationScore(1, 18));

        for (chosenDepth = 1; chosenDepth < 64;)
        {

            int tmp = searchBoard(chosenDepth++, 0, -30000000, 30000000, true);

            if (timer.MillisecondsElapsedThisTurn >= maxTimeMilliSeconds) break;


            bestEval = tmp;

#if STATS
            DebugPrints();
#endif
        }

        return moveToPlay.IsNull ? board.GetLegalMoves()[0] : moveToPlay;
    }

    public int searchBoard(int depth, int plyFromRoot, int alpha, int beta, bool nullMovePruningAllowed = true)
    {
        ulong currentKey = currentBoard.ZobristKey;
        int bestScore = -900000, newDepth = depth - 1, currentPly = currentBoard.PlyCount, moveCount;
        bool qsearch = depth <= 0, notRoot = plyFromRoot > 0;
        int colorMoving = currentBoard.IsWhiteToMove ? 1 : 0;

        if (notRoot && currentBoard.IsRepeatedPosition())
            return 0;

        TT_Entry ttEntry = transpositionTable[currentKey % (ttMask + 1)];

        if (notRoot && ttEntry.ZobristKey == currentKey && ttEntry.depth >= depth && (
            ttEntry.bound == 3 // exact score
                || ttEntry.bound == 2 && ttEntry.score >= beta // lower bound, fail high
                || ttEntry.bound == 1 && ttEntry.score <= alpha // upper bound, fail low
        ))
        {
#if Stats
            lookups++;
#endif
            return ttEntry.score;
        }

        int eval = Evaluate();

        if (qsearch)
        {
            bestScore = eval;
            if (bestScore >= beta) return bestScore;
            alpha = Math.Max(alpha, bestScore);

        }

        Move[] allMoves = currentBoard.GetLegalMoves(qsearch);
        int totalNumberOfMoves = allMoves.Length;
        int[] moveScores = new int[totalNumberOfMoves];


        for (int i = 0; i < totalNumberOfMoves; i++)
        {
            Move currentMove = allMoves[i];
            if (currentMove == ttEntry.move)
                moveScores[i] = 1000000;
            else if (currentMove.IsCapture)
                moveScores[i] = 500 * pieceValues[(int)currentMove.CapturePieceType] - (int)currentMove.MovePieceType;
            moveScores[i] += historyTable[colorMoving, (int)currentMove.MovePieceType, currentMove.TargetSquare.Index];
        }

        Move currentBestMove = Move.NullMove;
        int alphaOrig = alpha, moveScore;

        //Extension Checks
        if (currentBoard.IsInCheck()) newDepth++;

        //Null Move Pruning
        //Needs to not be on PVS. Preferred not in check

        if (depth >= 2 && beta - alpha <= 1 && nullMovePruningAllowed && currentBoard.TrySkipTurn())
        {
            int score = -searchBoard(depth - 3 - depth / 5, plyFromRoot + 1, -beta, -alpha, false);
            currentBoard.UndoSkipTurn();

            if (score >= beta) return score;
        }

        for (moveCount = 0; moveCount < totalNumberOfMoves; moveCount++)
        {
            if (currentTimer.MillisecondsElapsedThisTurn >= maxTimeMilliSeconds) return 999999;

            for (int i = moveCount + 1; i < totalNumberOfMoves; i++)
            {
                if (moveScores[i] > moveScores[moveCount])
                    (moveScores[i], moveScores[moveCount], allMoves[i], allMoves[moveCount]) = (moveScores[moveCount], moveScores[i], allMoves[moveCount], allMoves[i]);
            }

            Move move = allMoves[moveCount];

            currentBoard.MakeMove(move);
#if STATS
            nodes++;
#endif
            bool fullSearch = qsearch || moveCount == 0;

        Search:
            moveScore = -searchBoard(newDepth, plyFromRoot + 1, fullSearch ? -beta : (-alpha - 1), -alpha);

            if (!fullSearch && moveScore > alpha && moveScore < beta)
            {
                fullSearch = true;
                goto Search;
            }

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

                alpha = Math.Max(alpha, moveScore);

                if (alpha >= beta)
                {
                    if (!qsearch && !move.IsCapture) historyTable[colorMoving, (int)move.MovePieceType, move.TargetSquare.Index] += depth * depth;
                    break;
                }
            }

        }

        if (!qsearch && totalNumberOfMoves == 0) return currentBoard.IsInCheck() ? -900000 + plyFromRoot : 0;


#if STATS
        if (ttEntry.depth == 0) { 
            ttEntryCount++;
        }
#endif
        int boundType = bestScore >= beta ? 2 : bestScore > alphaOrig ? 3 : 1;

        transpositionTable[currentKey % (ttMask + 1)] = new TT_Entry(currentKey, currentBestMove, depth, bestScore, boundType);

        return bestScore;

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

                    mgScore += getLocationScore(pieceType, square) + pieceValues[pieceType];
                    egScore += getLocationScore(pieceType + 6, square) + pieceValues[pieceType];

                    //Console.WriteLine("square: " + square + " type: " + pieceType  + " score: " + toAddScore + "\n");
                    gamePhase += gamephaseInc[pieceType];

                }
            }
            mgScore = -mgScore;
            egScore = -egScore;
        }

        int score = (mgScore * gamePhase + egScore * (24 - gamePhase)) / 24;

        return score * (currentBoard.IsWhiteToMove ? 1 : -1);
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

        string lookupsString = "\x1b[37mlookups\x1b[34m " + lookups + "\x1b[37m";
        lookupsString += string.Concat(Enumerable.Repeat(" ", 32 - lookupsString.Length));

        string tablesizeString = "tablesize\x1b[31m " + ttEntryCount + "\x1b[37m";
        tablesizeString += string.Concat(Enumerable.Repeat(" ", 33 - tablesizeString.Length));

        Console.WriteLine(string.Join(" ", new string[] { depthString, timeString, bestMoveString, bestEvalString, nodesString, lookupsString, tablesizeString}));
    }
#endif

}