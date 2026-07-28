Bonjour,

J'ai eu plus de détails aujourd'hui et Central me confirme utiliser le standard OAuth + PKCE. De plus, OBO n'est pas utilisé.
Un appel côté serveur vers le /token de Azure est donc obligatoire est va sortir depuis les IPs publiques Croesus vers Azure. Je ne sais pas pourquoi cet appel est donc identifié comme une tentative de replay. 
Est-ce que OIDC est vraiment l'authentification attendue dans ce cas-ci? Est-ce que le /token devrait être fait sur un serveur chez Desjardins (proxy)? Est-ce que les IPs Croesus doivent être considérées comme connues pour éviter ces problèmes?

Nous restons disponibles pour régler cette situation le plus rapidement possible.

Merci et bonne journée,
Olivier Leblanc
Spécialiste, Infrastructure Cloud | Cloud Infrastructure Specialist



Croesus

LinkedIn   Youtube   Facebook
 Nos adresses
 450-662-6101
 1-855-243-6101


Le lun. 27 juill. 2026, à 10 h 55, Elisabeth Boivin <elisabeth.boivin@desjardins.com> a écrit :
Bonjour Olivier,

 

Avez-vous du nouveau à nous partager dans ce dossier, est-ce que votre service d’authentification a fait une nouvelle analyse de la situation?

 

Merci et bonne journée!

 



 

Une image contenant Police, Graphique, logo, capture d’écran

Description générée automatiquement

Elisabeth Boivin
Conseillère principale – responsable de produit

Équipe responsable de produits et Experts applicatifs

Section Solutions technologiques, placements et opérations sur titres   

 

DP Solutions d’affaires en gestion de patrimoine

VP Stratégie, Performance et innovation en gestion de patrimoine

 

 

 

 

 

 

2 complexe Desjardins, 17e étage, tour Est, Montréal, H5B 1E4

 

514 281-2244, poste 1234567

1 877 780-1171

Desjardins.com/gestiondepatrimoine

 

 

 

De : Elisabeth Boivin
Envoyé : 23 juillet 2026 11:01
À : 'Olivier Leblanc' <olivier.leblanc@croesus.com>; Annie Besner <annie.besner@croesus.com>; Mathieu Santerre <mathieu.santerre@desjardins.com>
Cc : Stephane Girard <stephane.girard@desjardins.com>; Emmanuel Knafo <Emmanuel.Knafo@microsoft.com>; Carl-Alexandre Simard <carl-alexandre.simard@desjardins.com>; Stephane Peladeau <stephane.peladeau@desjardins.com>; Angelique Welsch <angelique.welsch@desjardins.com>; Katy Campbell <katy.campbell@desjardins.com>; alexandre.rioux@croesus.com
Objet : RE: SSO environnement non prod

 

Bonjour Olivier,

 

Je laisse @Mathieu Santerre répondre à cette question pour Croesus Conseiller.

 

En attendant nous vous laissons faire vos validations, nous attendons de vos nouvelles rapidement.

 

N’hésitez pas si une rencontre est requise pour l’avancement du dossier.

 

Merci et bonne journée!

 

 



 

Une image contenant Police, Graphique, logo, capture d’écran

Description générée automatiquement

Elisabeth Boivin
Conseillère principale – responsable de produit

Équipe responsable de produits et Experts applicatifs

Section Solutions technologiques, placements et opérations sur titres   

 

DP Solutions d’affaires en gestion de patrimoine

VP Stratégie, Performance et innovation en gestion de patrimoine

 

 

 

 

 

 

2 complexe Desjardins, 17e étage, tour Est, Montréal, H5B 1E4

 

514 281-2244, poste 1234567

1 877 780-1171

Desjardins.com/gestiondepatrimoine

 

 

 

De : Olivier Leblanc <olivier.leblanc@croesus.com>
Envoyé : 23 juillet 2026 07:55
À : Annie Besner <annie.besner@croesus.com>
Cc : Mathieu Santerre <mathieu.santerre@desjardins.com>; Stephane Girard <stephane.girard@desjardins.com>; Emmanuel Knafo <Emmanuel.Knafo@microsoft.com>; Carl-Alexandre Simard <carl-alexandre.simard@desjardins.com>; Elisabeth Boivin <elisabeth.boivin@desjardins.com>; Stephane Peladeau <stephane.peladeau@desjardins.com>; Angelique Welsch <angelique.welsch@desjardins.com>; Katy Campbell <katy.campbell@desjardins.com>; alexandre.rioux@croesus.com
Objet : Re: SSO environnement non prod

 



COURRIEL EXTERNE CONTENANT UNE PIÈCE JOINTE OU UN LIEN URL / EXTERNAL EMAIL WITH AN ATTACHMENT OR A URL LINK
Soyez vigilant, développez les bons réflexes et ne téléchargez aucune pièce jointe non sollicitée! Au besoin, cliquez sur l’icône Signaler ou faites suivre ce courriel à protection@desjardins.com. / Be vigilant, develop the right instincts and do not download any unsolicited attachment! If required, click the Report button or forward the email at protection@desjardins.com

 

Merci Mathieu pour ces précisions. Ça change complètement ma compréhension de la problématique. Je vais m'assurer de transmettre ces détails aux équipes derrière les services d'authentification.

Etes-vous en mesure de confirmer que vous observez le même comportement pour Central et pour Conseiller?

 

Merci et bonne journée,

Olivier Leblanc

Spécialiste, Infrastructure Cloud | Cloud Infrastructure Specialist

 



Croesus

 

LinkedIn   Youtube   Facebook

 Nos adresses

 450-662-6101

 1-855-243-6101

 

 

Le mer. 22 juill. 2026, à 22 h 19, Annie Besner <annie.besner@croesus.com> a écrit :

+Alexandre R

 

Annie Besner
Gestionnaire de projets | Project manager |PSM
T  514-884-3250

 

Le mer. 22 juill. 2026, 8 h 03 p.m., Mathieu Santerre <mathieu.santerre@desjardins.com> a écrit :

Bonjour Olivier,

 

Pour clarifier, Desjardins n’a défini aucune des IP public de Croesus (PROD: 3.97.32.113 et 35.182.44.169  et/ou  NONPROD: 3.99.119.124 et 35.183.224.214) dans ses Azure Conditional Access Policies.

L’authentification au portail Croesus via SSO Azure (configuré vers notre Prod:  mvtdesjardins.com - 728d20a5-0b44-47dd-9470-20f37cbf2d9a) fonctionne uniquement à cause que les devices Desjardins sont considérés conformes (« compliant ») lorsque votre serveur dans AWS réutilise les tokens redirigés à lui par Azure lors de l’authentification SSO d’un client. À ce moment, votre serveur Croesus ne se contente pas de valider les tokens pour octroyer l’accès au client, il les réutilise pour s’authentifier lui aussi à notre Tenant Azure, sous l’identité du client (Token Protection sign-in-session status of Unbound (statusCode 1008)).  C’est là où ça bloque, lorsqu’on passe via notre Tenant de Non-Prod, car un devices peut uniquement joindre un seul AD/Tenant/politique de conformité Microsoft Intune. Donc, puisque nos devices de prod (laptop pour travailler), sont considérés non conformes (« non-compliant ») par nos Tenants de Non-Prod. Ils sont restreints à s’authentifier uniquement à partir du périmètre réseau dédié et contrôlé par Desjardins, ce qui exclut les authentifications provenant des IP public de Croesus.


Les politiques de conformité des appareils dans Microsoft Intune définissent ce qui constitue un appareil sécurisé pour une organisation. Ces politiques jouent le rôle de gardien, les appareils sont évalués selon des règles comme l'état de l'antivirus, l’encryptions, la version du système d'exploitation et les paramètres de sécurité, etc. Si un appareil respecte toutes les exigences, il est considéré comme conforme (« compliant »), sinon, il est considéré comme non conforme (« non-compliant »), ce qui bloquer l'authentification à nos tenants Azure, s’il est hors du périmètre réseau dédié et contrôlé par Desjardins.

L’accès à notre périmètre réseau Desjardins est géré par Network Access Control (NAC) qui vérifient qui et quoi se connecte au réseau, contrôlent si les appareils respectent les politiques de sécurité. Les appareils conformes obtiennent un accès, tandis que ceux qui ne le sont pas peuvent être bloqués, mis en quarantaine ou avoir un accès limité pour éviter les risques de sécurité. Un laptop ou autre device qui n’est pas Desjardins, est automatiquement bloqué et n’a aucun accès physique au réseau, ce pourquoi on garde confiance en nos devices provenant de notre périmètre pour accéder aux tenants de Non-Prod même si Azure lui ne peut pas les considérer comme conforme (« compliant »).

 

Merci et bonne journée,

 

Veuillez consulter notre Aide-mémoire – Wintel pour plus d’informations sur notre offre de service.

Heureux de travailler avec un membre de notre équipe?  Dites « BRAVO! »

 





 

 

 



Mathieu Santerre
Leader de pratique, Équipe Gestion des Authentifications et Automatisation Windows                

Services d’infrastructure

Technologies de l’information

Mouvement Desjardins

 

Montréal

1 514 281-7000, poste 5556133
1 866 866-7000, poste 5556133

 

 

From: Olivier Leblanc <olivier.leblanc@croesus.com>
Sent: July 20, 2026 3:47 PM
To: Stephane Girard <stephane.girard@desjardins.com>
Cc: Annie Besner <annie.besner@croesus.com>; Elisabeth Boivin <elisabeth.boivin@desjardins.com>; Stephane Peladeau <stephane.peladeau@desjardins.com>; Mathieu Santerre <mathieu.santerre@desjardins.com>; Angelique Welsch <angelique.welsch@desjardins.com>; Katy Campbell <katy.campbell@desjardins.com>
Subject: Re: SSO environnement non prod

 



COURRIEL EXTERNE CONTENANT UNE PIÈCE JOINTE OU UN LIEN URL / EXTERNAL EMAIL WITH AN ATTACHMENT OR A URL LINK
Soyez vigilant, développez les bons réflexes et ne téléchargez aucune pièce jointe non sollicitée! Au besoin, cliquez sur l’icône Signaler ou faites suivre ce courriel à protection@desjardins.com. / Be vigilant, develop the right instincts and do not download any unsolicited attachment! If required, click the Report button or forward the email at protection@desjardins.com

 

Bonjour,

 

Je crois que le problème avait déjà été identifié en début d'année (voir courriel ci-joint).

Croesus n'utilise pas les mêmes IPs publiques en sortie pour la prod et la nonprod et Desjardins n'a/avait pas encore autorisé les IPs utilisées en nonprod dans le SSO ce qui fait en sorte que la demande d'authentification soit refusée. C'est via ces IPs en sortie que l'appel d'API vers Azure est envoyé. C'est ce qui explique que les usagers ont l'air de se "téléporter" entre différents réseaux. Le serveur d'authentification d'Azure étant public, nous n'avons pas de route en place pour acheminer les appels au travers le VPN. Nous devons donc sortir par une passerelle internet.

 

À titre informatif, voici ce que nous avons actuellement de configuré en sortie:

PROD: 3.97.32.113 et 35.182.44.169
NONPROD: 3.99.119.124 et 35.183.224.214
Merci et bonne journée,

Olivier Leblanc

Spécialiste, Infrastructure Cloud | Cloud Infrastructure Specialist

 



Image removed by sender. Croesus

 

Image removed by sender. LinkedIn   Image removed by sender. Youtube   Image removed by sender. Facebook

Image removed by sender. Nos adresses

Image removed by sender. 450-662-6101

Image removed by sender. 1-855-243-6101

 

 

Le mer. 15 juill. 2026, à 13 h 38, Stephane Girard <stephane.girard@desjardins.com> a écrit :

Bonjour Annie,

 

Je vais être en vacances les deux prochaines semaines, je fais suivre ton courriel à Stephane Peladeau qui va me remplacer.

 

Je crois qu’il faut que Mathieu Santerre et l’ingénieur de Microsoft soient sur cette rencontre.

 

 

 

Veuillez prendre note que je serai en vacances du 20 au 31 juillet 2026 inclusivement.

 

Besoin d’une contribution ?



 





Stéphane Girard
Chef de mêlée

Entretien des systèmes,
Solutions aux entreprises
Technologies et Centre
de services partagés
Mouvement Desjardins


Montréal


 

Faites bonne impression et imprimez seulement au besoin!

 

Ce courriel est confidentiel, peut être protégé par le secret professionnel et est adressé exclusivement au destinataire. Il est

strictement interdit à toute autre personne de diffuser, distribuer ou reproduire ce message. Si vous l'avez reçu par erreur, veuillez

immédiatement le détruire et aviser l'expéditeur. Merci.

 

De : Annie Besner <annie.besner@croesus.com>
Envoyé : 15 juillet 2026 13:18
À : Elisabeth Boivin <elisabeth.boivin@desjardins.com>
Cc : Stephane Girard <stephane.girard@desjardins.com>; Angelique Welsch <angelique.welsch@desjardins.com>; Katy Campbell <katy.campbell@desjardins.com>; Olivier Leblanc <olivier.leblanc@croesus.com>
Objet : Re: SSO environnement non prod

 



COURRIEL EXTERNE CONTENANT UNE PIÈCE JOINTE OU UN LIEN URL / EXTERNAL EMAIL WITH AN ATTACHMENT OR A URL LINK
Soyez vigilant, développez les bons réflexes et ne téléchargez aucune pièce jointe non sollicitée! Au besoin, cliquez sur l’icône Signaler ou faites suivre ce courriel à protection@desjardins.com. / Be vigilant, develop the right instincts and do not download any unsolicited attachment! If required, click the Report button or forward the email at protection@desjardins.com

 

Allo Elisabeth,

 

Parfait, voici nos disponibilités:

- Mardi le 21 juillet à 11h pour 30 minutes

- Mercredi le 22 juillet à 10:30 ou 14h

 

Merci

 

 

 

 

Veuillez noter que je ne suis pas disponible le vendredi, mais pour toutes urgences vous pouvez me contacter par téléphone 

Please note that I am not available on Fridays, but for any emergencies, you can call me on my cell phone

 

Annie Besner

Gestionnaire principale de projets | Senior project manager

 



Croesus

 

LinkedIn   Youtube   Facebook

 Nos adresses

 514-884-3250 

 450-662-6101

 1-855-243-6101

 

 

Le mar. 14 juil. 2026 à 16:17, Elisabeth Boivin <elisabeth.boivin@desjardins.com> a écrit :

Allô Annie,

 

Nous comprenons que ce n’est pas parce que ça fonctionne pour d’autres clients (SSO avec Microsoft), que ça doit fonctionner automatiquement pour nous, tout dépend de l’option de connexion priorisée.

 

Desjardins a décidé de prioriser la connexion la plus sécurisée, nous avons relevé notre niveau de connexion, et ce niveau vient avec l’authentification qui cause des enjeux chez vous.  Selon Microsoft, il n’y a rien d’anormal dans le processus de connexion le plus sécurisé utilisé par Desjardins, il est conforme.

 

A ce que nous avons compris, d’autres clients pourraient dans un futur rapproché choisir eux aussi ce mode de connexion plus sécurisé, et ainsi avoir les mêmes enjeux que nous avons. 

 

Nous vous laissons prendre connaissance du retour Microsoft, une rencontre sera à prévoir dès que vous êtes disponibles (Croesus-Microsoft-Desjardins) pour la suite des choses.

 

Merci et bonne fin de journée



 

Une image contenant Police, Graphique, logo, capture d’écran

Description générée automatiquement

Elisabeth Boivin
Conseillère principale – responsable de produit

Équipe responsable de produits et Experts applicatifs

Section Solutions technologiques, placements et opérations sur titres   

 

DP Solutions d’affaires en gestion de patrimoine

VP Stratégie, Performance et innovation en gestion de patrimoine

 

 

 

 

 

 

2 complexe Desjardins, 17e étage, tour Est, Montréal, H5B 1E4

 

514 281-2244, poste 1234567

1 877 780-1171

Desjardins.com/gestiondepatrimoine

 

 

 

De : Annie Besner <annie.besner@croesus.com>
Envoyé : 13 juillet 2026 10:58
À : Stephane Girard <stephane.girard@desjardins.com>
Cc : Elisabeth Boivin <elisabeth.boivin@desjardins.com>; Angelique Welsch <angelique.welsch@desjardins.com>; Katy Campbell <katy.campbell@desjardins.com>; Olivier Leblanc <olivier.leblanc@croesus.com>
Objet : Re: SSO environnement non prod

 



COURRIEL EXTERNE CONTENANT UNE PIÈCE JOINTE OU UN LIEN URL / EXTERNAL EMAIL WITH AN ATTACHMENT OR A URL LINK
Soyez vigilant, développez les bons réflexes et ne téléchargez aucune pièce jointe non sollicitée! Au besoin, cliquez sur l’icône Signaler ou faites suivre ce courriel à protection@desjardins.com. / Be vigilant, develop the right instincts and do not download any unsolicited attachment! If required, click the Report button or forward the email at protection@desjardins.com

 

Allo Stéphane,

 

J'ajoute Olivier, spécialiste AWS à ce courriel.

 

Merci

 

Veuillez noter que je ne suis pas disponible le vendredi, mais pour toutes urgences vous pouvez me contacter par téléphone 

Please note that I am not available on Fridays, but for any emergencies, you can call me on my cell phone

 

Annie Besner

Gestionnaire principale de projets | Senior project manager

 



Croesus

 

LinkedIn   Youtube   Facebook

 Nos adresses

 514-884-3250 

 450-662-6101

 1-855-243-6101

 

 

Le lun. 13 juil. 2026 à 10:18, Stephane Girard <stephane.girard@desjardins.com> a écrit :

Bonjour Annie,

 

J’ai enfin reçu le courriel de Microsoft, est-ce que vous pouvez en prendre connaissance et nous revenir?

 

 

Dear Croesus team,

 

Microsoft has been engaged by Desjardins to review the identity and OAuth implementation of the Croesus SaaS platform as it integrates with Desjardins' Microsoft Entra tenant, and to confirm that it follows recommended practices and standards. As part of that mandate, we would like to schedule a working session with your engineering team.

 

Why we are reaching out

 

During sign-in analysis in a non-production environment, Desjardins observed a second, non-interactive sign-in originating from the Croesus AWS backend that was blocked by Conditional Access.

 

Our investigation of the Entra sign-in logs indicates that this second hop presents a replayed user token to Microsoft Graph rather than performing a standards-compliant OAuth 2.0 On-Behalf-Of (OBO) token exchange. The sign-in is recorded with a Token Protection sign-in-session status of Unbound (statusCode 1008), meaning the token is not bound to the originating device or platform.

 

We have reproduced this signal end to end and can walk you through the evidence.

 

We want to be clear that the Conditional Access block is correct by design and aligned with Zero Trust principles. Our goal is not to relax that control, but rather to align the Croesus flow with the OAuth 2.0 On-Behalf-Of standard so that the second hop issues a fresh, audience-bound token and the block is no longer triggered.

 

What we would like to review together

 

Server-side sign-in flow
The authoritative definition of the second (server-side) sign-in.
Whether it is intended to be a standards-compliant On-Behalf-Of exchange or a different server-side pattern.
The rationale behind the current implementation.
Entra app registration configuration
A confidential-client credential on the middle-tier/API registration (certificate preferred).
An exposed API scope (for example, access_as_user) so the front end requests an API-audienced token rather than a Graph-audienced token.
Pre-authorization of the client application on the API.
Resulting token characteristics on each leg
Distinct audience (aud) values.
A fresh jti and iat on the Graph-bound leg.
AWS network information
The AWS egress IP ranges used by the Croesus backend, so Desjardins can accurately correlate and scope the traffic.
To make the session productive, Microsoft has built a reference implementation of the corrected OBO architecture (a mock Croesus SPA and middle-tier API). This implementation demonstrates both the expected app registration configuration and how to validate that the token exchange is standards-compliant.

 

We would be happy to share this material and use it as a concrete starting point for the discussion.

 

Proposed next step

 

Could you propose a few times over the next two weeks for a 60–90 minute technical working session with your identity and integration engineers?

 

Please also let us know the best contact(s) on your side for OAuth and Entra app-registration configuration.

 

Thank you. We appreciate your partnership in aligning this implementation with recommended practices for the benefit of our shared customer, Desjardins.

 

--------------------------

 

Best regards,

 

Emmanuel Knafo, PhD
Sr. Cloud Solution Architect

Customer Success 
Mobile: 514.622.3180
emmanuel.knafo@microsoft.com

Microsoft Logo

 

 

 

 

Veuillez prendre note que je serai en vacances du 20 au 31 juillet 2026 inclusivement.

 

Besoin d’une contribution ?



 





Stéphane Girard
Chef de mêlée

Entretien des systèmes,
Solutions aux entreprises
Technologies et Centre
de services partagés
Mouvement Desjardins


Montréal

 

Faites bonne impression et imprimez seulement au besoin!

 

Ce courriel est confidentiel, peut être protégé par le secret professionnel et est adressé exclusivement au destinataire. Il est

strictement interdit à toute autre personne de diffuser, distribuer ou reproduire ce message. Si vous l'avez reçu par erreur, veuillez

immédiatement le détruire et aviser l'expéditeur. Merci.

 

 

_________________________________________________________________________

 

Avis : Ce message ne vise uniquement que son destinataire et peut contenir de l’information confidentielle.  Son utilisation/divulgation non-autorisée est interdite.  Si reçu par erreur, veuillez en aviser l’expéditeur et supprimer ce message et toute pièce jointe de votre système.

 

Notice:  This  message  is  intended  to  its  recipient  only  and  may  contain  privileged  information.   Unauthorized  use/disclosure is  prohibited.    If  received  by  error,  please  notify the  sender  and  delete this message and any attachments from your system.



Si vous avez reçu ce message électronique par erreur, ou si vous ne souhaitez plus recevoir de messages électroniques de notre part, veuillez nous en informer par courriel à l'adresse suivante: Desabonnement_Courriel@vmd.desjardins.com

If you have received this electronic message in error, or if you do not wish to receive any further electronic messages from us, please notify us by email at the following address: Email_Optout@vmd.desjardins.com

- L'intégrité des informations transmises dans ce courriel n’est pas garantie par Valeurs mobilières Desjardins, qui décline toute responsabilité quant aux dommages causés par leur modification frauduleuse. Ce courriel est confidentiel et est à l’usage exclusif de son destinataire. Toute personne qui reçoit celui-ci par erreur doit en informer immédiatement son expéditeur et le détruire sur-le-champ. Toute autre utilisation des informations qu’il contient est strictement interdite. Valeurs mobilières Desjardins se réserve le droit de surveiller toutes les communications transmises par courrier électronique par l’intermédiaire de ses réseaux. Les instructions relatives à des opérations acheminées par courrier électronique ne seront pas acceptées. Le présent avertissement ne limite aucunement tout autre avertissement plus restrictif qui vous aurait été transmis par Valeurs mobilières Desjardins.

- The integrity of the transmitted information in this email is not guaranteed by Desjardins Securities, which accepts no liability for any damage caused by its fraudulent alteration. This email is confidential and is intended for the sole use of the recipient or authorized representative of the recipient. Any person who receives this email by mistake shall immediately notify the sender and destroy it. Any other use of the information therein is strictly prohibited. Desjardins Securities reserves the right to monitor all communications sent by e-mail through its networks. Instructions related to operations sent by e-mail will not be accepted. In no way does this notice limit other more restrictive warnings that may have been sent to you by Desjardins Securities.

 

_________________________________________________________________________

 

Avis : Ce message ne vise uniquement que son destinataire et peut contenir de l’information confidentielle.  Son utilisation/divulgation non-autorisée est interdite.  Si reçu par erreur, veuillez en aviser l’expéditeur et supprimer ce message et toute pièce jointe de votre système.

 

Notice:  This  message  is  intended  to  its  recipient  only  and  may  contain  privileged  information.   Unauthorized  use/disclosure is  prohibited.    If  received  by  error,  please  notify the  sender  and  delete this message and any attachments from your system.

 

_________________________________________________________________________

 

Avis : Ce message ne vise uniquement que son destinataire et peut contenir de l’information confidentielle.  Son utilisation/divulgation non-autorisée est interdite.  Si reçu par erreur, veuillez en aviser l’expéditeur et supprimer ce message et toute pièce jointe de votre système.

 

Notice:  This  message  is  intended  to  its  recipient  only  and  may  contain  privileged  information.   Unauthorized  use/disclosure is  prohibited.    If  received  by  error,  please  notify the  sender  and  delete this message and any attachments from your system.

 

_________________________________________________________________________

 

Avis : Ce message ne vise uniquement que son destinataire et peut contenir de l’information confidentielle.  Son utilisation/divulgation non-autorisée est interdite.  Si reçu par erreur, veuillez en aviser l’expéditeur et supprimer ce message et toute pièce jointe de votre système.

 

Notice:  This  message  is  intended  to  its  recipient  only  and  may  contain  privileged  information.   Unauthorized  use/disclosure is  prohibited.    If  received  by  error,  please  notify the  sender  and  delete this message and any attachments from your system.



Si vous avez reçu ce message électronique par erreur, ou si vous ne souhaitez plus recevoir de messages électroniques de notre part, veuillez nous en informer par courriel à l'adresse suivante: Desabonnement_Courriel@vmd.desjardins.com

If you have received this electronic message in error, or if you do not wish to receive any further electronic messages from us, please notify us by email at the following address: Email_Optout@vmd.desjardins.com

- L'intégrité des informations transmises dans ce courriel n’est pas garantie par Valeurs mobilières Desjardins, qui décline toute responsabilité quant aux dommages causés par leur modification frauduleuse. Ce courriel est confidentiel et est à l’usage exclusif de son destinataire. Toute personne qui reçoit celui-ci par erreur doit en informer immédiatement son expéditeur et le détruire sur-le-champ. Toute autre utilisation des informations qu’il contient est strictement interdite. Valeurs mobilières Desjardins se réserve le droit de surveiller toutes les communications transmises par courrier électronique par l’intermédiaire de ses réseaux. Les instructions relatives à des opérations acheminées par courrier électronique ne seront pas acceptées. Le présent avertissement ne limite aucunement tout autre avertissement plus restrictif qui vous aurait été transmis par Valeurs mobilières Desjardins.

- The integrity of the transmitted information in this email is not guaranteed by Desjardins Securities, which accepts no liability for any damage caused by its fraudulent alteration. This email is confidential and is intended for the sole use of the recipient or authorized representative of the recipient. Any person who receives this email by mistake shall immediately notify the sender and destroy it. Any other use of the information therein is strictly prohibited. Desjardins Securities reserves the right to monitor all communications sent by e-mail through its networks. Instructions related to operations sent by e-mail will not be accepted. In no way does this notice limit other more restrictive warnings that may have been sent to you by Desjardins Securities.


_________________________________________________________________________

Avis : Ce message ne vise uniquement que son destinataire et peut contenir de l’information confidentielle.  Son utilisation/divulgation non-autorisée est interdite.  Si reçu par erreur, veuillez en aviser l’expéditeur et supprimer ce message et toute pièce jointe de votre système.

Notice:  This  message  is  intended  to  its  recipient  only  and  may  contain  privileged  information.   Unauthorized  use/disclosure is  prohibited.    If  received  by  error,  please  notify the  sender  and  delete this message and any attachments from your system.