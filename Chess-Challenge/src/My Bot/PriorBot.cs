using ChessChallenge.API;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

using System.IO;
using System.Drawing;

public class PriorBot : IChessBot
{
    // Piece values: pawn, knight, bishop, rook, queen, king
    int[] pieceValues = { 82, 337, 365, 477, 1025, 0, 94, 281, 297, 512, 936, 0 };

    struct TT_Entry
    {
        public ulong ZobristKey;
        public Move move;
        public int depth;

        public TT_Entry(ulong newKey, Move newMove, int depthDownTree)
        {
            ZobristKey = newKey;
            move = newMove;
            depth = depthDownTree;
        }

    };

    const ulong ttLength = 1048576;
    TT_Entry[] transpositionTable = new TT_Entry[ttLength];


    Move moveToPlay = Move.NullMove;
    int bestEval = 0;

    int subCount = 0;
    int nodes = 0;
    int chosenDepth = 0;

    // 400k mps

    // 64 bit conversion of the table
    int[] gamephaseInc = { 0, 1, 1, 2, 4, 0 };


    int[,,] PST = new int[2, 12, 64];

    bool stopped = false;


    ulong[] pieceTables = new ulong[96]
    {
        9259542123273814144,
        16357001140413309557,
        8829195605423724908,
        8254401669090808169,
        7313418688563415655,
        7384914337435197812,
        6737222767702746730,
        9259542123273814144,
        11081220660097301,
        3987876604305901423,
        5889764664614570412,
        8615829970516021910,
        8323936949179225464,
        7599697423020169584,
        7154940515368202861,
        1687519861085072745,
        7170907476791297912,
        7390528431378371153,
        8117082644194436478,
        8972740228098131838,
        8830870139635010180,
        9263780805260055178,
        9552012216381055361,
        6880781611516057963,
        11577242485982338987,
        11214168400861567660,
        8904630919850999184,
        7527071450275215468,
        6658137190229968489,
        6009895851999132511,
        6084482357074295353,
        7886789834650901350,
        7241961428581395373,
        7519176848743963830,
        8318016057608023993,
        7306369598957387393,
        8603695488949846909,
        8251286653394652805,
        6735286635485691265,
        9182408126746812750,
        4582289962092626573,
        11348908854965197411,
        8617781306342151786,
        8028920214486217308,
        5728408685346316109,
        8246770769504399717,
        9333560970455320968,
        8188824274210166926,
        9259542123273814144,
        18446744073709551615,
        16061197207704294100,
        11572154847021273489,
        10198820791839654783,
        9549736231487373176,
        10198551484841362041,
        9259542123273814144,
        5069491205526864157,
        7455822975978661964,
        7524541400582942039,
        8035431733674084462,
        7960834280560624750,
        7601372000551791722,
        6227482657520642388,
        7155491318799420992,
        8244812703126220648,
        8681963116054150258,
        9401405507207856260,
        9045915848980202370,
        8828055359350013303,
        8394015409149540721,
        8245661556352184677,
        7599658875216755567,
        10199125451369908357,
        10055849173233928323,
        9765923324499426173,
        9548631222335405954,
        9477131093459761269,
        8971306245150767216,
        8825507717624329597,
        8611590021041063020,
        8617240532094257812,
        8040227885603724928,
        7820089199224394633,
        9481933937386764708,
        7970407823045732247,
        8099037312892111493,
        7667768075636792416,
        6873735846648900695,
        3917408671579997295,
        8399651535887701899,
        9984928491787627661,
        8689300325139585667,
        7961402723962161525,
        7889615596832196471,
        7310895313622170479,
        5430896352595961941,
    };

    public PriorBot()
    {
        // Calculate PSTs from raw table once.

        for (int color = 0; color < 2; color++)
        {
            for (int pieceType = 0; pieceType < 6; pieceType++)
            {
                for (int square = 0; square < 64; square++)
                {

                    int shiftAmount = (7 - square % 8) * 8;
                    int locationPieceWeight = 4 * (int)(((pieceTables[(pieceType) * 8 + (7 - square / 8)] >> shiftAmount) & 0xFF) - 128);
                    PST[color, pieceType, square] = pieceValues[pieceType] + locationPieceWeight;

                    locationPieceWeight = 4 * (int)(((pieceTables[(pieceType + 6) * 8 + (7 - square / 8)] >> shiftAmount) & 0xFF) - 128);
                    PST[color, (pieceType + 6), square] = pieceValues[pieceType + 6] + locationPieceWeight;
                }
            }
        }

    }

    //Current Distance into the future 5 moves is comfortable
    // Debug Blunders
    public Move Think(Board board, Timer timer)
    {
        nodes = 0;
        int maxDepth = 64;
        stopped = false;

        for (chosenDepth = 1; chosenDepth < maxDepth; chosenDepth++)
        {

            bestEval = searchBoard(board, chosenDepth, timer, -3000000, 3000000);

            if (timer.MillisecondsElapsedThisTurn > timer.MillisecondsRemaining / 38)
                break;

            //Console.WriteLine("Depth: " + (chosenDepth) + " Best Eval: " + bestEval + " Moves " + nodes + " mps " + (nodes / Math.Max(timer.MillisecondsElapsedThisTurn, 1) * 1000).ToString());
        }

        //Console.WriteLine("Depth: " + (chosenDepth - 1) + " Best Eval: " + bestEval + " Moves " + nodes + " mps " + (nodes / timer.MillisecondsElapsedThisTurn * 1000).ToString());


        return moveToPlay;
    }

    int searchBoard(Board board, int depth, Timer timer, int alpha, int beta)
    {
        if ((nodes & 2047) == 0 && timer.MillisecondsElapsedThisTurn >= timer.MillisecondsRemaining / 20) stopped = true;
        bool qsearch = (depth <= 0);

        if (qsearch)
        {
            int standard = Evaluate(board);
            if (standard >= beta)
            {
                return beta;
            }
            if (alpha < standard)
            {
                alpha = standard;
            }
        }

        System.Span<Move> allMoves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref allMoves, qsearch);

        allMoves = orderedMoves(board, allMoves);

        /*Move[] allMoves;
        allMoves = board.GetLegalMoves(qsearch);*/

        if (allMoves.Length == 0)
        {
            if (board.IsDraw())
            {
                return 0;
            }
            return -900000 - depth;
        }

        int moveScore;
        Move currentBest = Move.NullMove;

        foreach (Move move in allMoves)
        {
            if (qsearch && !move.IsCapture)
            {
                //Console.WriteLine("QSEARCH ERROR " + move.ToString());
                continue;
            }

            board.MakeMove(move);
            nodes++;

            moveScore = -searchBoard(board, depth - 1, timer, -beta, -alpha);
            board.UndoMove(move);

            /*if(depth == chosenDepth)
            {
                Console.WriteLine(move.ToString() + " " + moveScore);
            }*/

            //if (stopped) return 0;

            if (moveScore >= beta) return beta;

            if (moveScore > alpha)
            {
                alpha = moveScore;
                currentBest = move;
                if (depth == chosenDepth)
                {
                    moveToPlay = move;
                }
            }

        }

        transpositionTable[board.ZobristKey % ttLength] = new(board.ZobristKey, currentBest, depth);


        return alpha;

    }


    Span<Move> orderedMoves(Board board, Span<Move> moves)
    {
        Span<int> moveScores = stackalloc int[moves.Length];
        TT_Entry ttEntry = transpositionTable[board.ZobristKey % ttLength];

        for (int i = 0; i < moves.Length; i++)
        {
            int score = 0;
            if (ttEntry.ZobristKey == board.ZobristKey && moves[i] == ttEntry.move)
            {
                score += 500000;
            }
            if (moves[i].IsCapture)
            {
                score += 50000 + 5 * pieceValues[(int)moves[i].CapturePieceType] - (int)moves[i].MovePieceType;
            }
            if (moves[i].IsPromotion)
            {
                score += 50000 + pieceValues[(int)moves[i].PromotionPieceType];
            }

            moveScores[i] = score;

        }

        MemoryExtensions.Sort<int, Move>(moveScores, moves);
        MemoryExtensions.Reverse<Move>(moves);

        //Console.Write("" + moveScores[0] + " " + moveScores[moves.Length-1] + "\n");

        return moves;
    }

    int Evaluate(Board board)
    {
        int sideToMove = (board.IsWhiteToMove ? 1 : -1);
        int[] mgScore = { 0, 0 }, egScore = { 0, 0 };
        int gamePhase = 0;

        for (int color = 0; color < 2; color++)
        {
            for (int pieceType = 0; pieceType < 6; pieceType++)
            {
                ulong pieceBB = board.GetPieceBitboard((PieceType)(pieceType + 1), color == 0);
                while (pieceBB != 0)
                {
                    int square = BitboardHelper.ClearAndGetIndexOfLSB(ref pieceBB) ^ (56 * color);
                    //Console.WriteLine("" + ((int)piece.PieceType - 1) + " " + modifiedRow + " " + piece.Square.File + "\n");

                    mgScore[color] += PST[color, pieceType, square];
                    egScore[color] += PST[color, (pieceType + 6), square];


                    //Console.WriteLine("square: " + square + " type: " + pieceType  + " score: " + toAddScore + "\n");
                    gamePhase += gamephaseInc[pieceType];

                }
            }
        }

        int score = ((mgScore[0] - mgScore[1]) * (gamePhase) + (egScore[0] - egScore[1]) * (24 - gamePhase)) / 24 * sideToMove;

        return score;
    }

}