namespace VectorClocks
{
    public interface IPartiallyComparable<in T>
    {
        int? PartiallyCompareTo(T other);
    }
}