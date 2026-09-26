namespace DrivingCoach.Overlay.Controls;

/// <summary>
/// Ringpuffer der letzten Pedal- und Lenkwerte für die Eingabe-Anzeige.
/// </summary>
/// <remarks>
/// Ein Ringpuffer statt einer Liste, weil bei 30 Hz sonst jede Sekunde 30
/// Elemente verschoben oder neu alloziert würden – das erzeugt nichts als
/// Müll für den GC, während der Fahrer Gleichmäßigkeit braucht.
/// </remarks>
public sealed class InputHistory
{
    private readonly float[] _throttle;
    private readonly float[] _brake;
    private readonly float[] _steering;

    private int _next;

    public InputHistory(int capacity = 180)
    {
        Capacity = capacity;
        _throttle = new float[capacity];
        _brake = new float[capacity];
        _steering = new float[capacity];
    }

    /// <summary>Wie viele Abtastungen der Puffer fasst.</summary>
    public int Capacity { get; }

    /// <summary>Wie viele Abtastungen aktuell belegt sind.</summary>
    public int Count { get; private set; }

    /// <summary>Wird nach jedem <see cref="Push"/> ausgelöst, damit die Anzeige neu zeichnet.</summary>
    public event Action? Updated;

    public void Push(float throttle, float brake, float steering)
    {
        _throttle[_next] = throttle;
        _brake[_next] = brake;
        _steering[_next] = steering;

        _next = (_next + 1) % Capacity;
        if (Count < Capacity)
        {
            Count++;
        }

        Updated?.Invoke();
    }

    public void Clear()
    {
        Array.Clear(_throttle);
        Array.Clear(_brake);
        Array.Clear(_steering);
        _next = 0;
        Count = 0;
        Updated?.Invoke();
    }

    /// <summary>Gasstellung, <paramref name="index"/> 0 ist der älteste Wert.</summary>
    public float Throttle(int index) => _throttle[Offset(index)];

    /// <summary>Bremsstellung, <paramref name="index"/> 0 ist der älteste Wert.</summary>
    public float Brake(int index) => _brake[Offset(index)];

    /// <summary>Lenkwinkel -1..1, <paramref name="index"/> 0 ist der älteste Wert.</summary>
    public float Steering(int index) => _steering[Offset(index)];

    private int Offset(int index)
    {
        int start = Count == Capacity ? _next : 0;
        return (start + index) % Capacity;
    }
}
