// SPDX-FileCopyrightText: 2026 DeegiTech Teknoloji ve Yazılım Ltd. Şti.
//
// SPDX-License-Identifier: AGPL-3.0-only

namespace Zadeh;

/// <summary>
/// The shape family of a membership function. Recorded by every <see cref="FuzzySet"/>
/// factory so sets can be serialized, visualized, and explained.
/// </summary>
public enum MembershipFunctionKind
{
    /// <summary>Triangle(left, peak, right)</summary>
    Triangle,
    /// <summary>Trapezoid(a, b, c, d)</summary>
    Trapezoid,
    /// <summary>LeftShoulder(midpoint, edge)</summary>
    LeftShoulder,
    /// <summary>RightShoulder(edge, midpoint)</summary>
    RightShoulder,
    /// <summary>Gaussian(mean, sigma)</summary>
    Gaussian
}

/// <summary>
/// A fuzzy set defined by a membership function that maps a crisp value to a degree of membership [0, 1].
/// 
/// Based on Lütfi Zadeh's fuzzy set theory (1965): instead of binary set membership (∈ or ∉),
/// an element belongs to a set with a degree μ ∈ [0, 1].
/// 
/// Supported membership function types:
///   - Triangle(a, m, b):       /\   — general purpose, peak at m
///   - Trapezoid(a, b, c, d):  /‾\   — plateau region for "definitely X"
///   - LeftShoulder(a, b):      ‾\   — open left ("Low", "Cold", "Cheap")
///   - RightShoulder(a, b):     /‾   — open right ("High", "Hot", "Expensive")
///   - Gaussian(mean, σ):       bell — smooth, natural distribution
/// 
/// All functions are deterministic and thread-safe.
/// </summary>
public sealed class FuzzySet
{
    /// <summary>The linguistic label for this set (e.g., "Low", "Medium", "High").</summary>
    public string Name { get; }

    /// <summary>The shape family this set was built from.</summary>
    public MembershipFunctionKind Kind { get; }

    /// <summary>
    /// The factory parameters this set was built with, in factory-argument order
    /// (e.g., Triangle → [left, peak, right]). Enables serialization and visualization.
    /// </summary>
    public IReadOnlyList<double> Parameters { get; }

    private readonly Func<double, double> _membershipFunction;

    private FuzzySet(string name, MembershipFunctionKind kind, double[] parameters, Func<double, double> membershipFunction)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Kind = kind;
        Parameters = parameters;
        _membershipFunction = membershipFunction ?? throw new ArgumentNullException(nameof(membershipFunction));
    }

    /// <summary>
    /// Computes the degree of membership for a given crisp value.
    /// Result is always clamped to [0, 1].
    /// </summary>
    public double Membership(double value) => Math.Clamp(_membershipFunction(value), 0.0, 1.0);

    // ─── Factory Methods ─────────────────────────────────────────────────

    /// <summary>
    /// Triangle membership function: rises from a to peak m, falls to b.
    /// <code>
    ///       /\
    ///      /  \
    ///     /    \
    /// ───a──m──b───
    /// </code>
    /// </summary>
    /// <param name="name">Linguistic label (e.g., "Medium")</param>
    /// <param name="left">Left foot — μ = 0</param>
    /// <param name="peak">Peak — μ = 1</param>
    /// <param name="right">Right foot — μ = 0</param>
    public static FuzzySet Triangle(string name, double left, double peak, double right)
    {
        if (left > peak || peak > right)
            throw new ArgumentException($"Triangle requires left ≤ peak ≤ right, got ({left}, {peak}, {right})");

        return new FuzzySet(name, MembershipFunctionKind.Triangle, new[] { left, peak, right }, x =>
        {
            if (x <= left || x >= right) return 0.0;
            if (x <= peak) return (x - left) / (peak - left);
            return (right - x) / (right - peak);
        });
    }

    /// <summary>
    /// Trapezoid membership function: plateau between b and c.
    /// <code>
    ///      ┌──────┐
    ///     /        \
    ///    /          \
    /// ──a──b────c──d──
    /// </code>
    /// </summary>
    public static FuzzySet Trapezoid(string name, double a, double b, double c, double d)
    {
        if (a > b || b > c || c > d)
            throw new ArgumentException($"Trapezoid requires a ≤ b ≤ c ≤ d, got ({a}, {b}, {c}, {d})");

        return new FuzzySet(name, MembershipFunctionKind.Trapezoid, new[] { a, b, c, d }, x =>
        {
            if (x <= a || x >= d) return 0.0;
            if (x >= b && x <= c) return 1.0;
            if (x < b) return (x - a) / (b - a);
            return (d - x) / (d - c);
        });
    }

    /// <summary>
    /// Left shoulder: full membership below midpoint, decreasing to edge.
    /// Use for "Low", "Cold", "Cheap" — open-ended on the left.
    /// <code>
    /// ──────┐
    ///        \
    ///         \
    /// ──mid──edge──
    /// </code>
    /// </summary>
    public static FuzzySet LeftShoulder(string name, double midpoint, double edge)
    {
        if (midpoint >= edge)
            throw new ArgumentException($"LeftShoulder requires midpoint < edge, got ({midpoint}, {edge})");

        return new FuzzySet(name, MembershipFunctionKind.LeftShoulder, new[] { midpoint, edge }, x =>
        {
            if (x <= midpoint) return 1.0;
            if (x >= edge) return 0.0;
            return (edge - x) / (edge - midpoint);
        });
    }

    /// <summary>
    /// Right shoulder: increasing from edge, full membership above midpoint.
    /// Use for "High", "Hot", "Expensive" — open-ended on the right.
    /// <code>
    ///          ┌──────
    ///         /
    ///        /
    /// ──edge──mid──
    /// </code>
    /// </summary>
    public static FuzzySet RightShoulder(string name, double edge, double midpoint)
    {
        if (edge >= midpoint)
            throw new ArgumentException($"RightShoulder requires edge < midpoint, got ({edge}, {midpoint})");

        return new FuzzySet(name, MembershipFunctionKind.RightShoulder, new[] { edge, midpoint }, x =>
        {
            if (x <= edge) return 0.0;
            if (x >= midpoint) return 1.0;
            return (x - edge) / (midpoint - edge);
        });
    }

    /// <summary>
    /// Gaussian (bell curve) membership function centered at mean with spread σ.
    /// Smaller σ = narrower peak, larger σ = wider spread.
    /// </summary>
    public static FuzzySet Gaussian(string name, double mean, double sigma)
    {
        if (sigma <= 0)
            throw new ArgumentException($"Gaussian requires sigma > 0, got {sigma}");

        return new FuzzySet(name, MembershipFunctionKind.Gaussian, new[] { mean, sigma }, x =>
        {
            var exponent = -0.5 * Math.Pow((x - mean) / sigma, 2);
            return Math.Exp(exponent);
        });
    }

    /// <summary>Returns a string representation of this fuzzy set.</summary>
    public override string ToString() => $"FuzzySet(\"{Name}\")";
}
