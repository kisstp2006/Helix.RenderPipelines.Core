#pragma once

#include <Helix/Core/Containers/Array.h>
#include <Helix/Engine/GameObject/Tags.h>
#include <Helix/Engine/Math/MathTransfer.h>
#include <Helix/Engine/Texture/Texture.h>
#include <Helix/Serialization/Transfer/Dynamic.h>

#include <type_traits>

// Volume parameters: the values of a volume component a volume overrides (its override state) and the stack blends
// (Interp from the stack's value to the profile's by the volume's weight). The typed parameters of the native kinds
// (bool, int, float, Vector2 to 4, Color, LayerMask, a texture) keep their values here; a C# parameter (any other
// type, and every parameter of a C# component) keeps its value in C#, reached through one set of callbacks.
namespace helix::rendering
{
    using engine::LayerMask;
    using engine::Texture;

    // A value of a native kind as C# reads and writes it: bools, ints and layer masks in i, vectors and colors in
    // x to w, a texture as its object id.
    HX_STRUCT(Namespace = "HelixEngine.Rendering.Internal", Internal)
    struct ParameterValue
    {
        HX_FIELD() f32 x = 0;
        HX_FIELD() f32 y = 0;
        HX_FIELD() f32 z = 0;
        HX_FIELD() f32 w = 0;
        HX_FIELD() i32 i = 0;
        HX_FIELD() u64 object = 0;
    };

    // The kind of a parameter's value (C# wraps a native parameter by it).
    enum class ParameterKind : i32
    {
        Bool,
        Int,
        Float,
        Vector2,
        Vector3,
        Vector4,
        Color,
        LayerMask,
        Texture,
        Managed,
    };

    class VolumeParameter
    {
    public:
        VolumeParameter();
        VolumeParameter(const VolumeParameter& other);
        VolumeParameter& operator=(const VolumeParameter& other);
        virtual ~VolumeParameter();

        // The parameter of an id (C#'s wrappers hold it); null once it is gone.
        static VolumeParameter* Find(u64 id);
        u64 Id() const { return id_; }

        virtual bool overrideState() const { return overrideState_; }
        virtual void SetOverrideState(bool value) { overrideState_ = value; }
        virtual ParameterKind kind() const = 0;

        // This parameter's value between two of its kind (the stack's own value and a profile's).
        virtual void Interp(const VolumeParameter& from, const VolumeParameter& to, f32 t) = 0;
        // The value of another parameter of its kind.
        virtual void SetValue(const VolumeParameter& parameter) = 0;
        virtual void Release() {}

        // The stack's parameter takes a profile's: its override state, and its value blended by t.
        virtual void OverrideFrom(const VolumeParameter& to, f32 t);
        // The stack's parameter back to a default (its value, not overridden).
        virtual void ResetTo(const VolumeParameter& defaults);

        // The value as C# reads and writes it (the native kinds).
        virtual ParameterValue Get() const { return {}; }
        virtual void Set(const ParameterValue&) {}
        // The limits a value is kept within (false: none), for C#'s min and max.
        virtual bool GetLimits(f32& min, f32& max) const
        {
            (void)min, (void)max;
            return false;
        }

    protected:
        bool overrideState_ = false;

    private:
        u64 id_ = 0;
    };

    namespace detail
    {
        template <class T>
        struct ParameterTraits;

        template <class T>
        T Lerp(const T& a, const T& b, f32 t);
    }

    // A parameter of a type: blends by taking the overriding value as soon as its weight is above zero (the typed
    // ones below lerp).
    template <class T>
    class VolumeParameterOf : public VolumeParameter
    {
    public:
        explicit VolumeParameterOf(T value = T(), bool overrideState = false) : value_(value) { overrideState_ = overrideState; }

        virtual T value() const { return value_; }
        virtual void SetValue(T value) { value_ = value; }
        void Override(T x)
        {
            SetOverrideState(true);
            value_ = x;
        }

        virtual void Interp(T from, T to, f32 t) { value_ = t > 0 ? to : from; }

        ParameterKind kind() const override { return detail::ParameterTraits<T>::kKind; }
        void Interp(const VolumeParameter& from, const VolumeParameter& to, f32 t) override
        {
            Interp(static_cast<const VolumeParameterOf&>(from).value_, static_cast<const VolumeParameterOf&>(to).value_, t);
        }
        void SetValue(const VolumeParameter& parameter) override { value_ = static_cast<const VolumeParameterOf&>(parameter).value_; }
        ParameterValue Get() const override { return detail::ParameterTraits<T>::Get(value_); }
        void Set(const ParameterValue& v) override { SetValue(detail::ParameterTraits<T>::Set(v)); }

    protected:
        T value_;
    };

    // The lerping kinds.
    template <class T>
    class LerpParameter : public VolumeParameterOf<T>
    {
    public:
        using VolumeParameterOf<T>::VolumeParameterOf;
        using VolumeParameterOf<T>::Interp;
        void Interp(T from, T to, f32 t) override { this->value_ = detail::Lerp(from, to, t); }
    };

    // A value kept within limits when it is set (not when it blends).
    template <class Base, class T>
    class Limited : public Base
    {
    public:
        Limited(T value, T min, T max, bool overrideState = false) : Base(value, overrideState), min(min), max(max) {}
        T min, max;
        T value() const override { return this->value_; }
        bool GetLimits(f32& lo, f32& hi) const override
        {
            lo = f32(min), hi = f32(max);
            return true;
        }
        void SetValue(T value) override { this->value_ = value < min ? min : value > max ? max : value; }
        using Base::SetValue;
    };

    HX_HANDLE(CSharp = "HelixEngine.Rendering.BoolParameter")
    using BoolParameter = VolumeParameterOf<bool>;
    HX_HANDLE(CSharp = "HelixEngine.Rendering.LayerMaskParameter")
    using LayerMaskParameter = VolumeParameterOf<LayerMask>;
    HX_HANDLE(CSharp = "HelixEngine.Rendering.IntParameter")
    using IntParameter = LerpParameter<i32>;
    HX_HANDLE(CSharp = "HelixEngine.Rendering.NoInterpIntParameter")
    using NoInterpIntParameter = VolumeParameterOf<i32>;
    HX_HANDLE(CSharp = "HelixEngine.Rendering.FloatParameter")
    using FloatParameter = LerpParameter<f32>;
    HX_HANDLE(CSharp = "HelixEngine.Rendering.NoInterpFloatParameter")
    using NoInterpFloatParameter = VolumeParameterOf<f32>;
    HX_HANDLE(CSharp = "HelixEngine.Rendering.Vector2Parameter")
    using Vector2Parameter = LerpParameter<Vector2>;
    HX_HANDLE(CSharp = "HelixEngine.Rendering.NoInterpVector2Parameter")
    using NoInterpVector2Parameter = VolumeParameterOf<Vector2>;
    HX_HANDLE(CSharp = "HelixEngine.Rendering.Vector3Parameter")
    using Vector3Parameter = LerpParameter<Vector3>;
    HX_HANDLE(CSharp = "HelixEngine.Rendering.NoInterpVector3Parameter")
    using NoInterpVector3Parameter = VolumeParameterOf<Vector3>;
    HX_HANDLE(CSharp = "HelixEngine.Rendering.Vector4Parameter")
    using Vector4Parameter = LerpParameter<Vector4>;
    HX_HANDLE(CSharp = "HelixEngine.Rendering.NoInterpVector4Parameter")
    using NoInterpVector4Parameter = VolumeParameterOf<Vector4>;

    HX_HANDLE(CSharp = "HelixEngine.Rendering.ClampedIntParameter")
    class ClampedIntParameter : public Limited<IntParameter, i32>
    {
    public:
        using Limited::Limited;
    };
    HX_HANDLE(CSharp = "HelixEngine.Rendering.ClampedFloatParameter")
    class ClampedFloatParameter : public Limited<FloatParameter, f32>
    {
    public:
        using Limited::Limited;
    };
    HX_HANDLE(CSharp = "HelixEngine.Rendering.NoInterpClampedFloatParameter")
    class NoInterpClampedFloatParameter : public Limited<NoInterpFloatParameter, f32>
    {
    public:
        using Limited::Limited;
    };
    HX_HANDLE(CSharp = "HelixEngine.Rendering.MinFloatParameter")
    class MinFloatParameter : public Limited<FloatParameter, f32>
    {
    public:
        MinFloatParameter(f32 value, f32 min, bool overrideState = false) : Limited(value, min, 3.402823466e+38f, overrideState) {}
    };
    HX_HANDLE(CSharp = "HelixEngine.Rendering.MaxFloatParameter")
    class MaxFloatParameter : public Limited<FloatParameter, f32>
    {
    public:
        MaxFloatParameter(f32 value, f32 max, bool overrideState = false) : Limited(value, -3.402823466e+38f, max, overrideState) {}
    };
    HX_HANDLE(CSharp = "HelixEngine.Rendering.MinIntParameter")
    class MinIntParameter : public Limited<IntParameter, i32>
    {
    public:
        MinIntParameter(i32 value, i32 min, bool overrideState = false) : Limited(value, min, 0x7fffffff, overrideState) {}
    };

    HX_HANDLE(CSharp = "HelixEngine.Rendering.ColorParameter")
    class ColorParameter : public LerpParameter<Color>
    {
    public:
        ColorParameter(Color value, bool overrideState = false) : LerpParameter(value, overrideState) {}
        ColorParameter(Color value, bool hdr, bool showAlpha, bool showEyeDropper, bool overrideState = false)
            : LerpParameter(value, overrideState), hdr(hdr), showAlpha(showAlpha), showEyeDropper(showEyeDropper)
        {
        }
        bool hdr = false, showAlpha = true, showEyeDropper = true;
    };

    // A texture by its object (a destroyed one reads null).
    HX_HANDLE(CSharp = "HelixEngine.Rendering.TextureParameter")
    class TextureParameter : public VolumeParameterOf<engine::EntityId>
    {
    public:
        explicit TextureParameter(Texture* value = nullptr, bool overrideState = false)
            : VolumeParameterOf(value ? value->GetEntityId() : engine::EntityId(), overrideState)
        {
        }
        Texture* texture() const { return engine::EntityIdToObject<Texture>(value_); }
        void SetTexture(Texture* t) { SetValue(t ? t->GetEntityId() : engine::EntityId()); }
        engine::TextureDimension dimension = engine::TextureDimension::Any;
    };

    // A C# parameter: its value, override state and blending are the C# object's (a GC handle the parameter frees).
    class ManagedParameter final : public VolumeParameter
    {
    public:
        struct Callbacks
        {
            u8 (*overrideState)(u64 handle) = nullptr;
            void (*setOverrideState)(u64 handle, u8 value) = nullptr;
            void (*interp)(u64 handle, u64 from, u64 to, f32 t) = nullptr;
            void (*setValue)(u64 handle, u64 from) = nullptr;
            void (*overrideFrom)(u64 handle, u64 to, f32 t) = nullptr;
            void (*resetTo)(u64 handle, u64 defaults) = nullptr;
            void (*release)(u64 handle) = nullptr;
            void (*free)(u64 handle) = nullptr;
        };
        static void SetCallbacks(const Callbacks& callbacks);

        explicit ManagedParameter(u64 handle) : handle_(handle) {}
        ~ManagedParameter() override;
        ManagedParameter(const ManagedParameter&) = delete;
        u64 handle() const { return handle_; }

        bool overrideState() const override;
        void SetOverrideState(bool value) override;
        ParameterKind kind() const override { return ParameterKind::Managed; }
        void Interp(const VolumeParameter& from, const VolumeParameter& to, f32 t) override;
        void SetValue(const VolumeParameter& parameter) override;
        void Release() override;
        void OverrideFrom(const VolumeParameter& to, f32 t) override;
        void ResetTo(const VolumeParameter& defaults) override;

    private:
        u64 handle_;
    };

    namespace detail
    {
        template <>
        struct ParameterTraits<bool>
        {
            static constexpr ParameterKind kKind = ParameterKind::Bool;
            static ParameterValue Get(bool v) { return {.i = v ? 1 : 0}; }
            static bool Set(const ParameterValue& v) { return v.i != 0; }
        };
        template <>
        struct ParameterTraits<i32>
        {
            static constexpr ParameterKind kKind = ParameterKind::Int;
            static ParameterValue Get(i32 v) { return {.i = v}; }
            static i32 Set(const ParameterValue& v) { return v.i; }
        };
        template <>
        struct ParameterTraits<LayerMask>
        {
            static constexpr ParameterKind kKind = ParameterKind::LayerMask;
            static ParameterValue Get(LayerMask v) { return {.i = v.value()}; }
            static LayerMask Set(const ParameterValue& v) { return LayerMask(v.i); }
        };
        template <>
        struct ParameterTraits<f32>
        {
            static constexpr ParameterKind kKind = ParameterKind::Float;
            static ParameterValue Get(f32 v) { return {.x = v}; }
            static f32 Set(const ParameterValue& v) { return v.x; }
        };
        template <>
        struct ParameterTraits<Vector2>
        {
            static constexpr ParameterKind kKind = ParameterKind::Vector2;
            static ParameterValue Get(Vector2 v) { return {.x = v.x, .y = v.y}; }
            static Vector2 Set(const ParameterValue& v) { return Vector2(v.x, v.y); }
        };
        template <>
        struct ParameterTraits<Vector3>
        {
            static constexpr ParameterKind kKind = ParameterKind::Vector3;
            static ParameterValue Get(Vector3 v) { return {.x = v.x, .y = v.y, .z = v.z}; }
            static Vector3 Set(const ParameterValue& v) { return Vector3(v.x, v.y, v.z); }
        };
        template <>
        struct ParameterTraits<Vector4>
        {
            static constexpr ParameterKind kKind = ParameterKind::Vector4;
            static ParameterValue Get(Vector4 v) { return {.x = v.x, .y = v.y, .z = v.z, .w = v.w}; }
            static Vector4 Set(const ParameterValue& v) { return Vector4(v.x, v.y, v.z, v.w); }
        };
        template <>
        struct ParameterTraits<Color>
        {
            static constexpr ParameterKind kKind = ParameterKind::Color;
            static ParameterValue Get(Color v) { return {.x = v.r, .y = v.g, .z = v.b, .w = v.a}; }
            static Color Set(const ParameterValue& v) { return Color(v.x, v.y, v.z, v.w); }
        };
        // An enum is of the int kind (C#'s VolumeParameter<the enum> reads and writes its number).
        template <class T>
            requires std::is_enum_v<T>
        struct ParameterTraits<T>
        {
            static constexpr ParameterKind kKind = ParameterKind::Int;
            static ParameterValue Get(T v) { return {.i = i32(v)}; }
            static T Set(const ParameterValue& v) { return T(v.i); }
        };
        template <>
        struct ParameterTraits<engine::EntityId>
        {
            static constexpr ParameterKind kKind = ParameterKind::Texture;
            static ParameterValue Get(engine::EntityId v) { return {.object = engine::EntityId::ToULong(v)}; }
            static engine::EntityId Set(const ParameterValue& v) { return engine::EntityId::FromULong(v.object); }
        };

        // The int parameters lerp as the reference's: (int)(from + (to - from) * t).
        template <>
        inline i32 Lerp(const i32& a, const i32& b, f32 t) { return i32(f32(a) + f32(b - a) * t); }
        template <>
        inline f32 Lerp(const f32& a, const f32& b, f32 t) { return a + (b - a) * t; }
        template <>
        inline Vector2 Lerp(const Vector2& a, const Vector2& b, f32 t) { return Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t); }
        template <>
        inline Vector3 Lerp(const Vector3& a, const Vector3& b, f32 t)
        {
            return Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }
        template <>
        inline Vector4 Lerp(const Vector4& a, const Vector4& b, f32 t)
        {
            return Vector4(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);
        }
        template <>
        inline Color Lerp(const Color& a, const Color& b, f32 t)
        {
            return Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        }
    }
}

// The parameter members of C++ volume components in files, in the reference's layout: m_OverrideState and m_Value (a
// layer mask as m_Bits, a texture as an object reference).
namespace helix::serialization
{
    template <class T>
        requires std::is_base_of_v<rendering::VolumeParameter, T> && requires(const T& p) { p.value(); }
    struct TransferTraits<T>
    {
        static constexpr StringView kTypeName = "VolumeParameter";

        struct Bits
        {
            static constexpr StringView kTypeName = "BitField";
            static constexpr i32 kVersion = 2;
            u32 bits = 0;
            template <class V>
            void Transfer(V& v)
            {
                v.Field("m_Bits", bits);
            }
        };

        template <class V>
        static void Transfer(V& v, T& p)
        {
            using Value = std::remove_cvref_t<decltype(p.value())>;
            bool overrides = p.overrideState();
            v.Field("m_OverrideState", overrides);
            p.SetOverrideState(overrides);
            if constexpr (std::is_same_v<Value, engine::EntityId>)
            {
                DynamicReferenceValue reference{engine::EntityId::ToULong(p.value())};
                v.Field("m_Value", reference);
                p.SetValue(engine::EntityId::FromULong(reference.id));
            }
            else if constexpr (std::is_same_v<Value, engine::LayerMask>)
            {
                Bits bits{u32(p.value().value())};
                v.Field("m_Value", bits);
                p.SetValue(engine::LayerMask(i32(bits.bits)));
            }
            else
            {
                Value value = p.value();
                v.Field("m_Value", value);
                p.SetValue(value);
            }
        }
    };
}
