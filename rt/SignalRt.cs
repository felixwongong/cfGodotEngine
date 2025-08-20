using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Rt;

public class SignalRt<[MustBeVariant]T>: Rt<T>
{
    public readonly Node sourceNode;
    public readonly StringName signalName;

    private Variant[] singleArgs;
    
    private Subscription _subscription;

    private SignalRt()
    {
        singleArgs = new Variant[1];
    }
    
    public SignalRt(Node sourceNode, StringName signalName): this()
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

public class SignalRt<[MustBeVariant]A, [MustBeVariant]B>: Rt<(A a, B b)>
{
    public readonly Node sourceNode;
    public readonly StringName signalName;
    
    private Variant[] valueArgs;
    
    private Subscription _subscription;
    
    public SignalRt()
    {
        valueArgs = new Variant[2];
    }

    public SignalRt(Node sourceNode, StringName signalName): this()
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
    }

    public void Set(A a, B b)
    {
        base.Set((a, b));
    }
}

public class PropertySignalRt<[MustBeVariant]T>: SignalRt<string, T>
{
    private readonly string propertyName;
    
    public PropertySignalRt(Node sourceNode, StringName signalName, string propertyName) : base(sourceNode, signalName)
    {
        this.propertyName = propertyName;
    }

    public void Set(T value)
    {
        Set(propertyName, value);
    }
}