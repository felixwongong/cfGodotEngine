using cfEngine.Rx;
using Godot;
using Godot.Collections;

namespace cfGodotEngine.Rx;

[System.AttributeUsage(System.AttributeTargets.Delegate, Inherited = false, AllowMultiple = false)]
public class CreateRtPropertyAttribute : System.Attribute
{
    public readonly string propertyName;

    public CreateRtPropertyAttribute(string propertyName)
    {
        this.propertyName = propertyName;
    }
}

public class SignalRx<[MustBeVariant]T>: Rx<T>
{
    public readonly Node sourceNode;
    public readonly StringName signalName;

    private Variant[] singleArgs;
    
    private Subscription _subscription;

    private SignalRx()
    {
        singleArgs = new Variant[1];
    }
    
    public SignalRx(Node sourceNode, StringName signalName): this()
    {
        this.sourceNode = sourceNode;
        this.signalName = signalName;

        _subscription = CollectionEvents.OnChange(EmitSignal);
    }

    public void EmitSignal()
    {
        var variant = Variant.From(Value);
        if (Value != null && variant.VariantType == Variant.Type.Nil)
        {
            GD.PrintErr("SignalRt: Value is not a valid Variant type. Value: ", Value, " Type: ", Value.GetType());
            return;
        }
        
        singleArgs[0] = variant;
        sourceNode.EmitSignal(signalName, singleArgs);
    }
}

public class SignalRx<[MustBeVariant]A, [MustBeVariant]B>: Rx<(A a, B b)>
{
    public readonly Node sourceNode;
    public readonly StringName signalName;
    
    private Variant[] valueArgs;
    
    private Subscription _subscription;
    
    public SignalRx()
    {
        valueArgs = new Variant[2];
    }

    public SignalRx(Node sourceNode, StringName signalName): this()
    {
        this.sourceNode = sourceNode;
        this.signalName = signalName;
        
        _subscription = CollectionEvents.OnChange(EmitSignal);
    }

    public void EmitSignal()
    {
        var variantA = Variant.From(Value.a);
        if (Value.a != null && variantA.VariantType == Variant.Type.Nil)
        {
            GD.PrintErr("SignalRt: Value.a is not a valid Variant type. Value: ", Value.a, " Type: ", Value.a.GetType());
            return;
        }
        
        var variantB = Variant.From(Value.b);
        if (Value.b != null && variantB.VariantType == Variant.Type.Nil)
        {
            GD.PrintErr("SignalRt: Value.b is not a valid Variant type. Value: ", Value.b, " Type: ", Value.b.GetType());
            return;
        }
        
        valueArgs[0] = variantA;
        valueArgs[1] = variantB;
        
        sourceNode.EmitSignal(signalName, valueArgs);

#if TOOLS
        if (EngineDebugger.IsActive())
        {
            EngineDebugger.SendMessage($"SignalRt:{sourceNode.Name}/{signalName}", new Array()
            {
                variantA,
                variantB
            });
        }
#endif
    }

    public void Set(A a, B b)
    {
        base.Set((a, b));
    }
}