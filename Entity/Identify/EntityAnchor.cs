using UnityEngine;

public enum EntityAnchor
{
    //完全不依赖人物骨骼的
    GameObjectPosition,
    Center,
    LabelFallBack,//伤害飘字回退位置

    //可依赖人物骨骼，但也支持非人形的
    Bar,//头顶血条位置
    UpCenter,//中上中心
    UpFront,//中上前方
    Camera,//相机注视点

    //必须依赖人物骨骼的
    ModelRootPosition,//模型根位置
    Head,
    LeftHand,
    RightHand,
    LeftFoot,
    RightFoot,
    Chest,
    WeaponHipLeft,
    WeaponHipRight,
    WeaponBack,
}
public struct EntityAnchorInfo
{
    public Transform transform;
    public Vector3 position;
    public Quaternion rotation;

    public static EntityAnchorInfo operator +(EntityAnchorInfo info, Vector3 offset)
    {
        return new EntityAnchorInfo()
        {
            transform = null,
            position = info.position + offset,
            rotation = info.rotation
        };
    }
}
public static class EntityAnchorExtensions
{
    public static EntityAnchorInfo GetTransform(this EntityAnchor anchor, EntityModelInfo model,EntityAnim anim)
    {
        if (anim != null)
        {
            switch (anchor)
            {
                case EntityAnchor.ModelRootPosition: return anim.GetBoneTransform(HumanBodyBones.Hips).ToAnchorInfo(model.transform.rotation);
                case EntityAnchor.Head: return anim.GetBoneTransform(HumanBodyBones.Head).ToAnchorInfo(model.transform.rotation);
                case EntityAnchor.LeftHand: return anim.GetBoneTransform(HumanBodyBones.LeftHand).ToAnchorInfo(model.transform.rotation);
                case EntityAnchor.RightHand: return anim.GetBoneTransform(HumanBodyBones.RightHand).ToAnchorInfo(model.transform.rotation);
                case EntityAnchor.LeftFoot: return anim.GetBoneTransform(HumanBodyBones.LeftFoot).ToAnchorInfo(model.transform.rotation);
                case EntityAnchor.RightFoot: return anim.GetBoneTransform(HumanBodyBones.RightFoot).ToAnchorInfo(model.transform.rotation);
                case EntityAnchor.Chest: return anim.GetBoneTransform(HumanBodyBones.Chest).ToAnchorInfo(model.transform.rotation);
                case EntityAnchor.WeaponHipLeft: return WeaponMount(anim.GetBoneTransform(HumanBodyBones.Hips), model, new Vector3(-HalfWidth(model) - 0.03f, -0.02f, -0.04f), WeaponHipTilt);
                case EntityAnchor.WeaponHipRight: return WeaponMount(anim.GetBoneTransform(HumanBodyBones.Hips), model, new Vector3(HalfWidth(model) + 0.03f, -0.02f, -0.04f), WeaponHipTilt);
                case EntityAnchor.WeaponBack: return WeaponMount(anim.GetBoneTransform(HumanBodyBones.Chest), model, new Vector3(0f, -0.05f, model.zRange.x - 0.08f), WeaponBackTilt);

                case EntityAnchor.Bar: 
                    {
                        var head = EntityAnchor.Head.GetTransform(model, anim);
                        var pos = new Vector3(model.transform.position.x, head.position.y + 0.5f, model.transform.position.z);
                        return new EntityAnchorInfo() { position =  pos,rotation=model.transform.rotation};
                    }
                case EntityAnchor.UpCenter: return EntityAnchor.Chest.GetTransform(model, anim);
                case EntityAnchor.UpFront: return EntityAnchor.Chest.GetTransform(model, anim) + (model.zRange.y+0.5f) * model.transform.forward;
                case EntityAnchor.Camera: return EntityAnchor.ModelRootPosition.GetTransform(model, anim) + (model.yRange.y * 0.5f + 0.5f) * Vector3.up;
            }
        }
        switch (anchor)
        {
            case EntityAnchor.GameObjectPosition: return model.transform.ToAnchorInfo();
            case EntityAnchor.Center: return model.transform.ToAnchorInfo() + (model.yRange.x + model.yRange.y) * 0.5f * Vector3.up;
            case EntityAnchor.LabelFallBack: return model.transform.ToAnchorInfo() + (model.yRange.y + 0.75f) * Vector3.up;

            case EntityAnchor.Bar: return model.transform.ToAnchorInfo() + (model.yRange.y + 0.5f) * Vector3.up;
            case EntityAnchor.UpCenter: return model.transform.ToAnchorInfo() + model.yRange.y*0.75f * Vector3.up;
            case EntityAnchor.UpFront: return model.transform.ToAnchorInfo() + model.yRange.y*0.75f * Vector3.up+ (model.zRange.y + 0.5f) * model.transform.forward;
            case EntityAnchor.Camera: return model.transform.ToAnchorInfo() + (model.yRange.y * 0.5f + 0.5f) * Vector3.up;
        }
        throw new System.Exception("Unknown anchor: " + anchor);
    }
    // 武器挂点：父节点必须是骨骼（武器要跟着人物动画动），位置 = 骨骼位置 + 实体空间偏移，朝向 = 实体朝向 × 固定倾斜
    // 武器预制体统一：z+ = 矛头/刀锋，刀刃竖直（刀面法线 = 局部 X）
    // 背上的长枪：矛头竖直并向右倾 30°；刀面法线朝后 → 刀面与背平行（不砍进背里）
    private static readonly Quaternion WeaponBackTilt = Quaternion.Euler(0f, 0f, -30f) * Quaternion.LookRotation(Vector3.up, Vector3.left);
    // 腰间的刀：先下压 30°，再转 180° 朝后 → 刀锋指向后下方，刀面贴着腰侧
    private static readonly Quaternion WeaponHipTilt = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(30f, 0f, 0f);

    private static float HalfWidth(EntityModelInfo model) => (model.xRange.y - model.xRange.x) * 0.5f;

    private static EntityAnchorInfo WeaponMount(Transform bone, EntityModelInfo model, Vector3 localOffset, Quaternion tilt)
    {
        Quaternion entityRotation = model.transform.rotation;
        return new EntityAnchorInfo()
        {
            transform = bone,
            position = bone.position + entityRotation * localOffset,
            rotation = entityRotation * tilt,
        };
    }
    // 骨骼锚点专用：旋转取骨骼所属实体（模型根），不取骨骼自身动画旋转
    private static EntityAnchorInfo ToAnchorInfo(this Transform transform, Quaternion entityRotation, Vector3 offset = default)
    {
        if(offset.sqrMagnitude<float.Epsilon)
        {
            return new EntityAnchorInfo()
            {
                transform = transform,
                position = transform.position,
                rotation = entityRotation
            };
        }
        return new EntityAnchorInfo()
        {
            transform = null,
            position = transform.position + offset,
            rotation = entityRotation
        };
    }
    private static EntityAnchorInfo ToAnchorInfo(this Transform transform,Vector3 offset = default)
    {
        if(offset.sqrMagnitude<float.Epsilon)
        {
            return new EntityAnchorInfo()
            {
                transform = transform,
                position = transform.position,
                rotation = transform.rotation
            };
        }
        return new EntityAnchorInfo()
        {
            transform = null,
            position = transform.position + offset,
            rotation = transform.rotation
        };
    }
}