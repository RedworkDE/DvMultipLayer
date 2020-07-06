using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using RedworkDE.DVMP.Networking;

namespace RedworkDE.DVMP
{
	/// <summary>
	/// Contains all general multiplayer related data
	/// </summary>
	public class MultiPlayerManager
	{
		private MultiPlayerManager()
		{

		}

		// singleton to enable possibly resetting after a multiplayer session
		public static MultiPlayerManager Instance { get; } = new MultiPlayerManager();

		private Action? _onBeforeMultiPlayerStarted;
		/// <summary>
		/// Multiplayer mode was requested
		/// </summary>
		public event Action OnBeforeMultiPlayerStarted
		{
			add
			{
				_onBeforeMultiPlayerStarted += value;
				if (_isMultiPlayer) _onBeforeMultiPlayerStarted?.Invoke();
			}
			remove
			{
				_onBeforeMultiPlayerStarted -= value;
			}
		}

		private Action? _onMultiPlayerStarted;
		/// <summary>
		/// First player has connected
		/// </summary>
		public event Action OnMultiPlayerStarted
		{
			add
			{
				_onMultiPlayerStarted += value;
				if (_isMultiPlayer) _onMultiPlayerStarted?.Invoke();
			}
			remove
			{
				_onMultiPlayerStarted -= value;
			}
		}

		private bool _isBeforeMultiPlayer;
		/// <summary>
		/// If multiplayer mode was requested
		/// </summary>
		public bool IsBeforeMultiPlayer
		{
			get => _isBeforeMultiPlayer;
			set
			{
				if (!_isBeforeMultiPlayer && value) _onBeforeMultiPlayerStarted?.Invoke();

				_isBeforeMultiPlayer = true;
			}
		}

		private bool _isMultiPlayer;
		/// <summary>
		/// If there are remote players
		/// </summary>
		public bool IsMultiPlayer
		{
			get => _isMultiPlayer;
			set
			{
				if (!_isMultiPlayer && value) _onMultiPlayerStarted?.Invoke();

				_isMultiPlayer = true;
			}
		}

#pragma warning disable 618
		[Obsolete("Use individual authorities instead")]
		public bool HasAuthority { get; set; }
		/// <summary>
		/// If this client assigns id blocks to everyone
		/// </summary>
		public bool HasIdAuthority => HasAuthority;
		/// <summary>
		/// If this client sends the initial junction state to new clients
		/// </summary>
		public bool HasJunctionAuthority => HasAuthority;
#pragma warning restore 618

		public IdDispenser Ids { get; } = new IdDispenser();

		private ClientId _ownId;
		/// <summary>
		/// Current ClientId
		/// </summary>
		public ClientId OwnId
		{
			get
			{
				if (_ownId == default && HasIdAuthority)
				{
					var id = NewId();
					_ownId = Unsafe.As<MultiPlayerId, ClientId>(ref id);
				}
				return _ownId;
			}
			internal set
			{
				if (_ownId == default) _ownId = value;
			}
		}

		private ClientId _idAuthority;
		/// <summary>
		/// Client that has the IdAuthority
		/// </summary>
		public ClientId IdAuthority
		{
			get
			{
				return _idAuthority;
			}
			internal set
			{
				if (_idAuthority == default) _idAuthority = value;
			}
		}
		
		/// <summary>
		/// Get a new id that can be used to determine the identity of multiplayer objects
		/// </summary>
		/// <returns></returns>
		public MultiPlayerId NewId()
		{
			if (!IsBeforeMultiPlayer)
			{
				Logging<MultiPlayerManager>.Logger.LogError($"Requested id when not in multi player mode: {new StackTrace(1, true)}");
				return default;
			}
			return Ids.GetId();
		}

		/// <summary>
		/// RemotePlayer instance for the current player
		/// </summary>
		public RemotePlayer? LocalPlayer { get; set; }

		/// <summary>
		/// Remote players, not including the current one
		/// </summary>
		public readonly Dictionary<ClientId, RemotePlayer> RemotePlayers = new Dictionary<ClientId, RemotePlayer>();

		/// <summary>
		/// All players, including the current one
		/// </summary>
		public readonly Dictionary<ClientId, RemotePlayer> Players = new Dictionary<ClientId, RemotePlayer>();
	}

	public class IdDispenser : HasLogger<IdDispenser>
	{
		public const uint BLOCK_SIZE = 1024;
		public const uint FALLBACK_BLOCKS_START_OFFSET = 0xff000000;
		public const uint FALLBACK_BLOCKS_START = FALLBACK_BLOCKS_START_OFFSET / BLOCK_SIZE;
		public const uint FALLBACK_BLOCKS_END = uint.MaxValue / BLOCK_SIZE;
		
		private readonly Queue<uint> _allocatedBlocks = new Queue<uint>();
		private Block? _currentBlock;

		private int _allocatedBlock;

		public MultiPlayerId GetId()
		{
			while (true)
			{
				// local copy to avoid replacement of the object
				var block = _currentBlock;

				// a currently active block exists
				if (block is {})
				{
					// try to take the next id in the block
					var seq = (uint) Interlocked.Increment(ref block.Fill);
					// when the block is full delete it (unless it was already changed)
					if (seq > BLOCK_SIZE)
						Interlocked.CompareExchange(ref _currentBlock, null, block);
					// Interlocked::Increment returns the incremented value, undo this here to not skip the first block
					else
						return MakeId(block.Offset + seq - 1);
				}

				lock (_allocatedBlocks)
				{
					// already allocated a block
					if (_currentBlock is {}) continue;

					if (_allocatedBlocks.TryDequeue(out var nextBlock) || MultiPlayerManager.Instance.HasIdAuthority && (nextBlock = AllocateBlock()) != 0)
					{
						// take a new pre allocated block
						_currentBlock = new Block(nextBlock * BLOCK_SIZE);
					}
					else
					{
						// no blocks available create a fallback one
						var off = (uint) UnityEngine.Random.Range(FALLBACK_BLOCKS_START, FALLBACK_BLOCKS_END) * BLOCK_SIZE;
						Logger.LogWarning($"no blocks allocated, using fallback {off:x6}");
						_currentBlock = new Block(off + FALLBACK_BLOCKS_START_OFFSET);
					}
				}

				// after we started a new block, request a new one to refill the queue if necessary
				if (_allocatedBlocks.Count < 10) RequestBlock();
			}
		}

		/// <summary>
		/// Creates a MultiPlayerId that has the given value, since it is not normally possible to create non zero instances
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private MultiPlayerId MakeId(uint value)
		{
			return Unsafe.As<uint, MultiPlayerId>(ref value);
		}

		public void RequestBlock()
		{
			if (MultiPlayerManager.Instance.HasIdAuthority)
			{
				AcquireBlock(AllocateBlock());
			}
			else
			{
				NetworkManager.Send(new RequestIdBlockPacket(), MultiPlayerManager.Instance.IdAuthority);
			}
		}
		
		public void AcquireBlock(uint block)
		{
			if (block == 0) return;
			lock (_allocatedBlocks) _allocatedBlocks.Enqueue(block);
		}

		internal uint AllocateBlock()
		{
			var block = (uint)Interlocked.Increment(ref _allocatedBlock); // no decrement here, block 0 is never allocated
			if (block < FALLBACK_BLOCKS_START) return block;
			// ran out of blocks
			// avoid eventually overflowing
			_allocatedBlock = (int)FALLBACK_BLOCKS_START;
			return 0; 
		}

		private class Block
		{
			public Block(uint offset)
			{
				Offset = offset;
			}

			public readonly uint Offset;
			public int Fill;
		}

	}

	/// <summary>
	/// Opaque ids for networked objects
	/// if the instance casts to false, it is not a valid MultiPlayerId
	/// </summary>
	public readonly struct MultiPlayerId : IEquatable<MultiPlayerId>, IComparable<MultiPlayerId>
	{
		public int CompareTo(MultiPlayerId other)
		{
			return __value.CompareTo(other.__value);
		}

		public static bool operator <(MultiPlayerId left, MultiPlayerId right)
		{
			return left.CompareTo(right) < 0;
		}

		public static bool operator >(MultiPlayerId left, MultiPlayerId right)
		{
			return left.CompareTo(right) > 0;
		}

		public static bool operator <=(MultiPlayerId left, MultiPlayerId right)
		{
			return left.CompareTo(right) <= 0;
		}

		public static bool operator >=(MultiPlayerId left, MultiPlayerId right)
		{
			return left.CompareTo(right) >= 0;
		}

		public bool Equals(MultiPlayerId other)
		{
			return __value == other.__value;
		}

		public override bool Equals(object? obj)
		{
			return obj is MultiPlayerId other && Equals(other);
		}

		public override int GetHashCode()
		{
			// todo: investigate better hash code generation, but uint::GetHashCode() is the same as this
			return (int) __value; 
		}

		public static bool operator ==(MultiPlayerId left, MultiPlayerId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(MultiPlayerId left, MultiPlayerId right)
		{
			return !left.Equals(right);
		}

		public static implicit operator bool(MultiPlayerId self)
		{
			return self.__value >= IdDispenser.BLOCK_SIZE;
		}

#pragma warning disable 649
		private readonly uint __value;
#pragma warning restore 649

		public override string ToString()
		{
			return __value.ToString("x8");
		}
	}
}