using cfEngine.Pooling;
using Godot;

namespace cfGodotEngine.Tool;

[Tool]
[GlobalClass]
public partial class SimpleRecyclable2D: Node2D
{
   [Signal] public delegate void OnInitializeEventHandler();
   [Signal] public delegate void OnClearEventHandler();
   
   private ObjectPool<SimpleRecyclable2D> _pool;
   
   public void SetPool(ObjectPool<SimpleRecyclable2D> pool)
   {
      _pool = pool;
   }

   public virtual void Initialize()
   {
      EmitSignalOnInitialize();
   }
   
   public void Recycle()
   {
      Clear();
      _pool?.Release(this);
   }

   public virtual void Clear()
   {
      EmitSignalOnClear();
   }
}