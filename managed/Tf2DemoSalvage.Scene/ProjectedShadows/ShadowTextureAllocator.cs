using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>
/// The render-to-texture shadow atlas's cells — <c>CTextureAllocator</c> (`clientshadowmgr.cpp:117-673`, finding 107).
/// </summary>
/// <remarks>
/// **One 1024² page, sixteen 256² blocks, each cut into cells of one size**: block 0 into 16² cells, block 1 into 32²,
/// blocks 2–3 into 64², 4–9 into 128², 10–15 into one 256² cell each (`Reset`, `:271-316`). A cell size keeps its own
/// least-recently-used list; a texture asks for a cell by its screen area each frame (<see cref="UseTexture"/>), keeps one
/// that is close enough, and otherwise takes the oldest cell of the size it wants — or a smaller one when every cell of that
/// size was used this frame. Only the bookkeeping is here; the page itself is the renderer's.
/// </remarks>
public sealed class ShadowTextureAllocator
{
    /// <summary>The page's side, in texels.</summary>
    public const int PageSize = 1024;

    private const int MaxPower = 8;
    private const int MinPower = 4;
    private const int MaxSize = 1 << MaxPower;
    private const int MinSize = 1 << MinPower;
    private const int BlocksPerRow = PageSize / MaxSize;
    private const int BlockCount = BlocksPerRow * BlocksPerRow;
    private const int None = -1;

    /// <summary>`m_FrameUsed` of a cell never used — `0xFFFFFFFF`, which no frame counter reaches.</summary>
    private const uint NeverUsed = uint.MaxValue;

    /// <summary>Each block's cell size as a power of two (`Reset`, `:281-299`).</summary>
    private static readonly int[] BlockPower = [4, 5, 6, 6, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8];

    private struct Fragment
    {
        public int Block;
        public int Index;
        public int Texture;
        public uint FrameUsed;
        public int Previous;
        public int Next;
    }

    private struct Texture
    {
        public int Fragment;
        public int Power;
        public bool Live;
    }

    private readonly List<Fragment> _fragments = [];
    private readonly List<Texture> _textures = [];
    private readonly Stack<int> _freeTextures = new();
    private readonly int[] _head = new int[MaxPower + 1];
    private readonly int[] _tail = new int[MaxPower + 1];
    private uint _currentFrame;

    /// <summary>A page with every cell free — <c>Reset</c>.</summary>
    public ShadowTextureAllocator() => Reset();

    /// <summary>Frees every texture and cell and lays the blocks out again — <c>Reset</c> (`:271-316`).</summary>
    public void Reset()
    {
        _fragments.Clear();
        _textures.Clear();
        _freeTextures.Clear();
        Array.Fill(_head, None);
        Array.Fill(_tail, None);

        for (int block = 0; block < BlockCount; block++)
        {
            AddBlockToLru(block);
        }

        _currentFrame = 0;
    }

    /// <summary>A texture of a size, clamped to the cells' range — <c>AllocateTexture</c> (`:425-453`).</summary>
    /// <param name="size">Its side in texels, a power of two.</param>
    /// <returns>Its handle; it has no cell until <see cref="UseTexture"/>.</returns>
    public int AllocateTexture(int size)
    {
        size = Math.Clamp(size, MinSize, MaxSize);

        int power = 0;

        while ((1 << power) < size)
        {
            power++;
        }

        Texture texture = new() { Fragment = None, Power = power, Live = true };

        if (_freeTextures.TryPop(out int reused))
        {
            _textures[reused] = texture;
            return reused;
        }

        _textures.Add(texture);
        return _textures.Count - 1;
    }

    /// <summary>Frees a texture; its cell goes to the head of its list — <c>DeallocateTexture</c> (`:455-470`).</summary>
    /// <param name="handle">The texture.</param>
    public void DeallocateTexture(int handle)
    {
        int fragment = _textures[handle].Fragment;

        if (fragment != None)
        {
            Unlink(fragment);
            LinkToHead(fragment);
            Fragment info = _fragments[fragment];
            info.FrameUsed = NeverUsed;
            _fragments[fragment] = info;
            Disconnect(fragment);
        }

        _textures[handle] = default;
        _freeTextures.Push(handle);
    }

    /// <summary>Whether a texture holds a cell — <c>HasValidTexture</c>.</summary>
    /// <param name="handle">The texture.</param>
    /// <returns>True when it has one.</returns>
    public bool HasValidTexture(int handle) => _textures[handle].Fragment != None;

    /// <summary>Marks a texture used this frame and finds it a cell for its screen area — <c>UseTexture</c> (`:508-611`).</summary>
    /// <param name="handle">The texture.</param>
    /// <param name="willRedraw">Whether its shadow is drawn this frame anyway, which makes moving it free.</param>
    /// <param name="area">Its screen area, the square of its diameter in pixels.</param>
    /// <returns>True when it took a new cell, which must be drawn.</returns>
    public bool UseTexture(int handle, bool willRedraw, float area)
    {
        Texture texture = _textures[handle];

        int desiredPower = MinPower;
        int desiredWidth = MinSize;

        while ((float)desiredWidth * desiredWidth < area)
        {
            if (desiredPower >= texture.Power)
            {
                desiredPower = texture.Power;
                break;
            }

            desiredPower++;
            desiredWidth <<= 1;
        }

        int currentPower = -1;
        int current = texture.Fragment;

        if (current != None)
        {
            currentPower = BlockPower[_fragments[current].Block];
            bool keep = !willRedraw && desiredPower < MaxPower && desiredPower - currentPower <= 1;

            if (currentPower == desiredPower || keep)
            {
                MarkUsed(current);
                return false;
            }
        }

        int power = desiredPower;
        int found = None;

        // "Used too many textures of the same size in a single frame": a smaller size.
        while (power >= 0)
        {
            found = _head[power];

            if (found != None && _fragments[found].FrameUsed != _currentFrame)
            {
                break;
            }

            power--;
        }

        if (current != None)
        {
            if (power <= currentPower)
            {
                MarkUsed(current);
                return false;
            }

            Disconnect(current);
        }

        if (power < 0 || found == None)
        {
            return false;
        }

        Disconnect(found);

        texture = _textures[handle];
        texture.Fragment = found;
        _textures[handle] = texture;

        Fragment cell = _fragments[found];
        cell.Texture = handle;
        _fragments[found] = cell;

        MarkUsed(found);
        return true;
    }

    /// <summary>The next frame — <c>AdvanceFrame</c>, once a frame and not once a view (`:626-631`).</summary>
    public void AdvanceFrame() => _currentFrame++;

    /// <summary>Where a texture's cell is on the page — <c>GetTextureRect</c> (`:654-673`).</summary>
    /// <param name="handle">A texture holding a cell.</param>
    /// <returns>Its left, top, width and height in texels.</returns>
    /// <exception cref="InvalidOperationException">The texture has no cell.</exception>
    public (int X, int Y, int Width, int Height) TextureRect(int handle)
    {
        int fragment = _textures[handle].Fragment;

        if (fragment == None)
        {
            throw new InvalidOperationException("The texture holds no cell; ask HasValidTexture first.");
        }

        Fragment info = _fragments[fragment];
        int blockY = info.Block / BlocksPerRow;
        int blockX = info.Block - (blockY * BlocksPerRow);
        int size = 1 << BlockPower[info.Block];
        int perRow = MaxSize / size;
        int cellY = info.Index / perRow;
        int cellX = info.Index - (cellY * perRow);

        return ((blockX * MaxSize) + (cellX * size), (blockY * MaxSize) + (cellY * size), size, size);
    }

    /// <summary>`AddBlockToLRU` (`:361-381`): the block's cells from the last index down, each to the head.</summary>
    private void AddBlockToLru(int block)
    {
        int size = 1 << BlockPower[block];
        int count = MaxSize / size;
        count *= count;

        while (--count >= 0)
        {
            _fragments.Add(new Fragment { Block = block, Index = count, Texture = None, FrameUsed = NeverUsed, Previous = None, Next = None });
            LinkToHead(_fragments.Count - 1);
        }
    }

    /// <summary>`MarkUsed` (`:396-405`): to the tail of its size's list, stamped with this frame.</summary>
    private void MarkUsed(int fragment)
    {
        Unlink(fragment);
        LinkToTail(fragment);
        Fragment info = _fragments[fragment];
        info.FrameUsed = _currentFrame;
        _fragments[fragment] = info;
    }

    /// <summary>`DisconnectTextureFromFragment` (`:476-491`).</summary>
    private void Disconnect(int fragment)
    {
        Fragment info = _fragments[fragment];

        if (info.Texture != None)
        {
            Texture owner = _textures[info.Texture];
            owner.Fragment = None;
            _textures[info.Texture] = owner;
            info.Texture = None;
            _fragments[fragment] = info;
        }
    }

    private void LinkToHead(int fragment)
    {
        int power = BlockPower[_fragments[fragment].Block];
        Fragment info = _fragments[fragment];
        info.Previous = None;
        info.Next = _head[power];
        _fragments[fragment] = info;

        if (_head[power] != None)
        {
            Fragment next = _fragments[_head[power]];
            next.Previous = fragment;
            _fragments[_head[power]] = next;
        }
        else
        {
            _tail[power] = fragment;
        }

        _head[power] = fragment;
    }

    private void LinkToTail(int fragment)
    {
        int power = BlockPower[_fragments[fragment].Block];
        Fragment info = _fragments[fragment];
        info.Next = None;
        info.Previous = _tail[power];
        _fragments[fragment] = info;

        if (_tail[power] != None)
        {
            Fragment previous = _fragments[_tail[power]];
            previous.Next = fragment;
            _fragments[_tail[power]] = previous;
        }
        else
        {
            _head[power] = fragment;
        }

        _tail[power] = fragment;
    }

    private void Unlink(int fragment)
    {
        int power = BlockPower[_fragments[fragment].Block];
        Fragment info = _fragments[fragment];

        if (info.Previous != None)
        {
            Fragment previous = _fragments[info.Previous];
            previous.Next = info.Next;
            _fragments[info.Previous] = previous;
        }
        else if (_head[power] == fragment)
        {
            _head[power] = info.Next;
        }

        if (info.Next != None)
        {
            Fragment next = _fragments[info.Next];
            next.Previous = info.Previous;
            _fragments[info.Next] = next;
        }
        else if (_tail[power] == fragment)
        {
            _tail[power] = info.Previous;
        }

        info.Previous = None;
        info.Next = None;
        _fragments[fragment] = info;
    }
}
