# Sun Shade control

Covers the logic for controllinng sun shades that extract from the facade outwards, casting a shadow to the ground.

## gemini

[Gemini chat](https://gemini.google.com/share/e1dfe89fef66)

Non-verified - preserved for later. There's indeed some weird stuff in it.

### Prompt

Ich habe eine Sonnenstore, die im Winkel von 20° ab der horizontalen nach unten ausgefahren werden kann. Sie ist an einer Facade die ein Azimuth von 210° von Norden her hat. Ist die Store 100% ausgefahren, ragt sie 1.5m nach vorne. Die Store wirft den Schatten auf den Boden und ist in 2m Höhe montiert. Gegeben die relative Sonnenposition in °deg azimuth und elevation, möchte ich die Store nur soweit ausfahren, dass der Schatten unter deren Befestigung ist (0% ausgefahren), nicht weiter vorne, nicht weiter hinten, so der Fahrbereich der Store erlaubt. Erläutere mir die Berechnung.

Korrektur:
Die Schattentiefe ist falsch berechnet. Die Schattentiefe misst sich von der Vorderkante der Pergola in die Pergola hinein, wenn die Sonne von Vorne hineinscheint. E.g. bei tiefem Sonnenstand. Die Rückwand der Pergola spielt keine Rolle. Die Sonnenmarkise verlängert quasi die Vorderkante der Pergola, so dass der Boden der Pergola Schatten hat trotz Sonne von vorne.

### Answer

Du hast vollkommen recht – mein Ansatz mit der Rückwand war zu restriktiv. Wenn es darum geht, die Schattentiefe innerhalb der Pergola ab der Vorderkante (Traufe) zu steuern, müssen wir die Geometrie so betrachten, dass die Markise als "Schattenwerfer" fungiert, der den Schattenwurf der Pergola-Vorderkante ergänzt.

#### Das neue geometrische Modell
Betrachten wir die Pergola als Dach mit der Höhe H und einer festen Tiefe. Die Sonne scheint unter dem Winkel γ 
S
​
  herein. Die Pergola-Vorderkante wirft ohne Markise bereits einen Schatten der Tiefe S 
Pergola
​
 = 
tan(γ 
S
​
 )
H
​
  in die Pergola hinein.

Du möchtest nun, dass dieser Schatten bis zu einer gewünschten Tiefe T (vom Rand der Pergola nach innen gemessen) reicht.

Die Markise wird an der Vorderkante montiert, fährt um x aus und hat die Neigung β. Ihre Vorderkante befindet sich auf der Höhe H 
Markise
​
 =H−x⋅tan(β).
Der Schatten dieser neuen Vorderkante muss nun die Distanz T erreichen.

Die Gleichung für den Schattenwurf der Markisen-Vorderkante lautet:

T= 
tan(γ 
S
​
 )
H−x⋅tan(β)
​
 ⋅cos(Δα)
Wir lösen nach x auf:

T⋅tan(γ 
S
​
 )=(H−x⋅tan(β))⋅cos(Δα)
T⋅tan(γ 
S
​
 )=H⋅cos(Δα)−x⋅tan(β)⋅cos(Δα)
x⋅tan(β)⋅cos(Δα)=H⋅cos(Δα)−T⋅tan(γ 
S
​
 )
x= 
tan(β)⋅cos(Δα)
H⋅cos(Δα)−T⋅tan(γ 
S
​
 )
​
 
#### C# Methode für die gewünschte Schattentiefe

Hier ist die angepasste Logik. Du kannst nun den Parameter targetShadowDepth definieren (die Tiefe in Metern, die ab der Pergola-Vorderkante beschattet sein soll).
​
```csharp
public static double CalculateAwningForShadowDepth(
    double sunAzimuthDegrees, 
    double sunElevationDegrees, 
    double targetShadowDepth, // Gewünschte Schattentiefe in Metern ab Pergola-Vorderkante (in welche Richtung??)
    double facadeAzimuthDegrees = 210.0,
    double mountHeight = 2.0,
    double maxExtension = 1.5,
    double awningAngleDegrees = 20.0)
{
    if (sunElevationDegrees <= 0) return 0.0;

    double deltaAzimuth = (sunAzimuthDegrees - facadeAzimuthDegrees + 540) % 360 - 180;
    if (Math.Abs(deltaAzimuth) >= 90.0) return 0.0;

    double deltaAzimuthRad = deltaAzimuth * (Math.PI / 180.0);
    double elevationRad = sunElevationDegrees * (Math.PI / 180.0);
    double awningAngleRad = awningAngleDegrees * (Math.PI / 180.0);

    double cosDA = Math.Cos(deltaAzimuthRad);
    double tanEl = Math.Tan(elevationRad);
    double tanBeta = Math.Tan(awningAngleRad);

    // Wenn der natürliche Schatten der Pergola-Vorderkante (H/tan(El)) 
    // bereits tiefer als T ist, brauchen wir die Markise nicht.
    if ((mountHeight / tanEl) * cosDA >= targetShadowDepth) return 0.0;

    // Formel: x = (H * cos(DA) - T * tan(El)) / (tan(Beta) * cos(DA))
    double numerator = (mountHeight * cosDA) - (targetShadowDepth * tanEl);
    double denominator = tanBeta * cosDA;

    if (denominator <= 0) return 0.0; 

    double x = numerator / denominator;
    
    return Math.Clamp((x / maxExtension) * 100.0, 0.0, 100.0);
}
```
