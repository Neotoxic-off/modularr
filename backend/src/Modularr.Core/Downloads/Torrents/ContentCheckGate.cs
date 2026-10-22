using MonoTorrent;
using MonoTorrent.PiecePicking;

namespace Modularr.Core.Downloads.Torrents;

// Requests no piece until opened: a magnet gets its metadata, then nothing more until its file list passed the content check.
// MonoTorrent starts downloading right after the metadata, before any outside code can set the file priorities.
internal sealed class ContentCheckGate(IPieceRequester inner) : IPieceRequester
{
    private volatile bool _open;

    public bool InEndgameMode => inner.InEndgameMode;

    public void Open() => _open = true;

    public void AddRequests(ReadOnlySpan<(IRequester Peer, ReadOnlyBitField Available)> peers)
    {
        if (_open)
        {
            inner.AddRequests(peers);
        }
    }

    public void AddRequests(IRequester peer, ReadOnlyBitField available, ReadOnlySpan<ReadOnlyBitField> allPeers)
    {
        if (_open)
        {
            inner.AddRequests(peer, available, allPeers);
        }
    }

    public bool IsInteresting(IRequester peer, ReadOnlyBitField bitfield) => inner.IsInteresting(peer, bitfield);

    public void Initialise(IPieceRequesterData torrentData, IMessageEnqueuer enqueuer, ReadOnlySpan<ReadOnlyBitField> ignoringBitfields) =>
        inner.Initialise(torrentData, enqueuer, ignoringBitfields);

    public bool ValidatePiece(IRequester peer, PieceSegment pieceSegment, out bool pieceComplete, HashSet<IRequester> peersInvolved) =>
        inner.ValidatePiece(peer, pieceSegment, out pieceComplete, peersInvolved);

    public void CancelRequests(IRequester peer, int startIndex, int endIndex) => inner.CancelRequests(peer, startIndex, endIndex);

    public void RequestRejected(IRequester peer, PieceSegment pieceRequest) => inner.RequestRejected(peer, pieceRequest);

    public int CurrentRequestCount() => inner.CurrentRequestCount();
}
