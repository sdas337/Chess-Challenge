using ChessChallenge.API;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

using System.IO;
using System.Threading;
using static System.Formats.Asn1.AsnWriter;

public class ChallengerBot : IChessBot
{
    // Piece values: null, pawn, knight, bishop, rook, queen, king
    int[] pieceValues = { 0, 126, 781, 825, 1276, 2538, 20000 };
    Dictionary<ulong, Tuple<int, int>> openWith;
    int subCount = 0;
    int nodes = 0;


    // Consider 4 bitboard style ulongs to represent the table.

    // 64 bit conversion of the table
    ulong[] pieceTables = new ulong[56]
    {
        9259542123273814144,
        12876550765177647794,
        9982954933006404234,
        9621248571257554309,
        9259542209508704384,
        9618411723462966149,
        9622655752112605829,
        9259542123273814144,
        5645370307605846094,
        6371608862222478424,
        7097825361929076834,
        7099238255929820514,
        7097830881046265954,
        7099232736812631394,
        6371608883781200984,
        5645370307605846094,
        7815564454513768044,
        8538966182894534774,
        8538971723570446454,
        8540379098454001014,
        8538977221128913014,
        8541791970896022134,
        8540373557778089334,
        7815564454513768044,
        9259542123273814144,
        9622655881464941189,
        8899254153084174459,
        8899254153084174459,
        8899254153084174459,
        8899254153084174459,
        8899254153084174459,
        9259542144832536704,
        7815564476072490604,
        8538966182894534774,
        8538971702011723894,
        8899259672201363579,
        9259547642391003259,
        8540379076895277174,
        8538971680452673654,
        7815564476072490604,
        7086511107012581474,
        7086511107012581474,
        7086511107012581474,
        7086511107012581474,
        7809912835393348204,
        8533314606891560054,
        10706323503566591124,
        10709149248450633364,
        5645370350723291214,
        7092173807484824674,
        7095021671955068514,
        7095032710189446754,
        7095032710189446754,
        7095021671955068514,
        7089370052834648674,
        5648185057372955214,
    };

    public ChallengerBot()
    {
        openWith = new Dictionary<ulong, Tuple<int, int>>();
    }

    //Current Distance into the future 5 moves is comfortable
    public Move Think(Board board, ChessChallenge.API.Timer timer)
    {
        nodes = 0;

        if (openWith.Count > 5000000)
        {
            openWith = new Dictionary<ulong, Tuple<int, int>>();
            Console.Write("Reset Dictionary\n");
        }

        Move[] allMoves = orderedMoves(board.GetLegalMoves());
        int moveScore;

        // NegaMax Style Search
        Move moveToPlay = allMoves[0];
        int bestMove = -2147483647;
        int depth = 4;

        foreach (Move move in allMoves)
        {

            board.MakeMove(move);
            nodes++;
            moveScore = -searchBoard(board, depth - 1, -2147483647, 2147483647);
            board.UndoMove(move);

            //Console.WriteLine("" + board.PlyCount + " " + moveScore + move.StartSquare.ToString() + " " + move.TargetSquare.ToString());
            if (moveScore > bestMove)
            {
                bestMove = moveScore;
                moveToPlay = move;
            }
            openWith[board.ZobristKey] = new Tuple<int, int>(4, moveScore);
        }
        //Console.Write("Challenger: " + moveToPlay.ToString() + " " + bestMove + "\n");
        //Console.Write(board.GetPiece(moveToPlay.StartSquare).PieceType + "\n");

        Console.WriteLine("Depth: " + depth + " Best Eval: " + bestMove + " Moves " + nodes + " mps " + (nodes / Math.Max(timer.MillisecondsElapsedThisTurn, 1) * 1000).ToString());
        Console.WriteLine("~~~~~~~~~~~~~");
        return moveToPlay;
    }

    int searchBoard(Board board, int depth, int alpha, int beta)
    {
        if (depth == 0)
        {
            //return Evaluate(board);
            return QuiescenceSearch(board, alpha, beta);
        }
        if (board.IsDraw()) return 0;


        Move[] allMoves = board.GetLegalMoves();
        allMoves = orderedMoves(allMoves);

        int moveScore;

        foreach (Move move in allMoves)
        {
            board.MakeMove(move);
            nodes++;
            moveScore = -searchBoard(board, depth - 1, -beta, -alpha);
            board.UndoMove(move);

            if (moveScore >= beta) return beta;



            if (moveScore > alpha)
                alpha = moveScore;

        }

        openWith[board.ZobristKey] = new Tuple<int, int>(depth, alpha);

        return alpha;

    }

    int QuiescenceSearch(Board board, int alpha, int beta)
    {
        //Perform limited search 
        int standard = Evaluate(board);
        if (standard >= beta)
        {
            return beta;
        }
        if (alpha < standard)
        {
            alpha = standard;
        }

        //Get Captures
        Move[] allCaptures = board.GetLegalMoves(true);
        allCaptures = orderedMoves(allCaptures);

        foreach (Move move in allCaptures)
        {
            board.MakeMove(move);
            int moveScore = -QuiescenceSearch(board, -beta, -alpha);
            board.UndoMove(move);

            if (moveScore >= beta) return beta;

            if (moveScore > alpha)
                alpha = moveScore;

        }
        return alpha;
    }

    Move[] orderedMoves(Move[] moves)
    {
        int[] moveScores = new int[moves.Length];

        for (int i = 0; i < moves.Length; i++)
        {
            if (moves[i].IsCapture)
            {
                moveScores[i] = 50000 + 5 * pieceValues[(int)moves[i].CapturePieceType] - (int)moves[i].MovePieceType;
            }
            if (moves[i].IsPromotion)
            {
                moveScores[i] += 50000 + pieceValues[(int)moves[i].PromotionPieceType];
            }

            /*if (openWith.ContainsKey(board.ZobristKey))
                moveScore = openWith[board.ZobristKey];*/
        }

        for (int i = 0; i < moves.Length; i++)
        {
            for (int j = i + 1; j < moves.Length; j++)
            {
                if (moveScores[i] < moveScores[j])
                {
                    int tmp = moveScores[i];
                    moveScores[i] = moveScores[j];
                    moveScores[j] = tmp;

                    Move holder = moves[i];
                    moves[i] = moves[j];
                    moves[j] = holder;
                }
            }
        }

        //Console.Write("" + moveScores[0] + " " + moveScores[moves.Length-1] + "\n");

        return moves;
    }


    int Evaluate(Board board)
    {
        int sideToMove = (board.IsWhiteToMove ? 1 : -1);
        int score, whiteScore = 0, blackScore = 0, pieceCount = 0;

        //Piece Value (Value is how much more value does white have over black)
        PieceList[] pieces = board.GetAllPieceLists();
        for (int i = 0; i < 6; i++)
        {
            whiteScore += pieceValues[i + 1] * (pieces[i].Count);
            blackScore += pieceValues[i + 1] * pieces[i + 6].Count;
            pieceCount += pieces[i].Count + pieces[i + 6].Count;
        }
        score = (whiteScore - blackScore) * sideToMove;

        int pieceScore = 0;
        int endgameMod = 0;
        //Location
        for (int i = 1; i < 7; i++)
        {
            PieceList chosenPieceType = board.GetPieceList((PieceType)i, board.IsWhiteToMove);
            foreach (Piece piece in chosenPieceType)
            {
                int row = piece.Square.Rank;
                int modifiedRow = (piece.IsWhite) ? piece.Square.Rank : 7 - row;
                //Console.WriteLine("" + ((int)piece.PieceType - 1) + " " + modifiedRow + " " + piece.Square.File + "\n");

                if (i == 6 && pieceCount < 12)
                {
                    endgameMod = 1;
                    //Console.WriteLine("Evaluating Endgame");
                }

                ulong tempScore = pieceTables[((int)piece.PieceType - 1 + endgameMod) * 8 + (modifiedRow)];
                int shiftAmount = (8 * (7 - piece.Square.File));
                int scoreToAdd = (int)(tempScore & (((ulong)0xFF) << shiftAmount)) >> shiftAmount;
                pieceScore = pieceScore + scoreToAdd;

                //Console.WriteLine("" + tempScore + " " + scoreToAdd + " " + piece.Square.File + "\n");
            }
        }

        int[] scoreWeights = new int[] { 10, 3 };

        //Console.WriteLine("" + score + " " + (scoreWeights[0] * score) + " " + pieceScore + " " + (scoreWeights[1] * pieceScore) + "\n");

        score = scoreWeights[0] * score + scoreWeights[1] * pieceScore;

        return score;
    }

}